using System.Text;
using System.Threading.RateLimiting;
using Dapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Transcendence.Api.Auth;
using Transcendence.Api.Data;
using Transcendence.Api.Matches;
using Transcendence.Api.Users;

DefaultTypeMap.MatchNamesWithUnderscores = true; // password_hash -> PasswordHash

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- data
builder.Services.AddSingleton(services =>
{
    var connectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is not set");
    return NpgsqlDataSource.Create(connectionString);
});
builder.Services.AddSingleton<Migrator>();
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<MatchRepository>();

// ---------------------------------------------------------------- auth
builder.Services.AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection(JwtSettings.Section))
    .Validate(settings => Encoding.UTF8.GetByteCount(settings.Key) >= 32, "Jwt:Key must be at least 32 bytes")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IPasswordHasher<UserRecord>, PasswordHasher<UserRecord>>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, jwt) =>
    {
        var settings = jwt.Value;
        options.MapInboundClaims = false; // keep "sub" and "name" as they are in the token
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
        };
    });
builder.Services.AddAuthorization();

// Brute force protection on /api/auth: a fixed window per client IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
    {
        var perMinute = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimiting:AuthPerMinute", 10);
        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
});

// ---------------------------------------------------------------- web
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "https://localhost:8443" };
builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

await app.Services.GetRequiredService<Migrator>().RunAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapMatchEndpoints();

await app.RunAsync();

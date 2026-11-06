using Microsoft.AspNetCore.Identity;
using Transcendence.Api.Users;

namespace Transcendence.Api.Auth;

public static class AuthEndpoints
{
    public sealed record Credentials(string? Username, string? Password);

    public sealed record RegisteredUser(long Id, string Username);

    public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth").WithTags("Auth");
        auth.MapPost("/register", Register);
        auth.MapPost("/login", Login);
        return app;
    }

    private static async Task<IResult> Register(
        Credentials body, UserRepository users, IPasswordHasher<UserRecord> hasher, CancellationToken ct)
    {
        var errors = CredentialRules.Validate(body.Username, body.Password);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }
        var hash = hasher.HashPassword(new UserRecord(), body.Password!);
        var user = await users.CreateAsync(body.Username!, hash, ct);
        if (user is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "This username is already taken");
        }
        return TypedResults.Created($"/api/users/{user.Username}", new RegisteredUser(user.Id, user.Username));
    }

    private static async Task<IResult> Login(
        Credentials body, UserRepository users, IPasswordHasher<UserRecord> hasher, TokenService tokens, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(body.Username) || string.IsNullOrEmpty(body.Password))
        {
            return WrongCredentials();
        }
        var user = await users.FindByUsernameAsync(body.Username, ct);
        if (user is null)
        {
            // Same hashing work as a real check, so the answer time does not reveal which usernames exist.
            hasher.HashPassword(new UserRecord(), body.Password);
            return WrongCredentials();
        }
        var check = hasher.VerifyHashedPassword(user, user.PasswordHash, body.Password);
        if (check == PasswordVerificationResult.Failed)
        {
            return WrongCredentials();
        }
        if (check == PasswordVerificationResult.SuccessRehashNeeded)
        {
            await users.UpdatePasswordHashAsync(user.Id, hasher.HashPassword(user, body.Password), ct);
        }
        var (token, expiresAt) = tokens.Create(user);
        return TypedResults.Ok(new TokenResponse(token, expiresAt));
    }

    // One message for "no such user" and "wrong password": the API never says which one it was.
    private static IResult WrongCredentials() =>
        TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Wrong username or password");
}

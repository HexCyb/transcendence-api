using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Transcendence.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFixture api)
{
    [Fact]
    public async Task Register_then_login_gives_a_working_token()
    {
        var name = TestData.Username("alice");
        var client = await api.Factory.LoggedInClientAsync(name);

        var me = await client.GetJsonAsync<ProfileBody>("/api/users/me");

        Assert.Equal(name, me.Username);
        Assert.Equal(1000, me.Rating);
    }

    [Fact]
    public async Task Usernames_are_unique_ignoring_case()
    {
        var client = api.Factory.CreateClient();
        var name = TestData.Username("bob");

        var first = await client.PostAsJsonAsync("/api/auth/register", new { username = name, password = TestData.Password });
        var again = await client.PostAsJsonAsync("/api/auth/register", new { username = name.ToUpperInvariant(), password = TestData.Password });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Theory]
    [InlineData("ab", "long-enough-42")]
    [InlineData("bad name!", "long-enough-42")]
    [InlineData("valid_name", "short42")]
    [InlineData("valid_name", "only-letters-here")]
    public async Task Weak_credentials_are_refused(string username, string password)
    {
        var response = await api.Factory.CreateClient().PostAsJsonAsync("/api/auth/register", new { username, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_get_the_same_answer()
    {
        var client = api.Factory.CreateClient();
        var name = TestData.Username("carol");
        await client.PostAsJsonAsync("/api/auth/register", new { username = name, password = TestData.Password });

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { username = name, password = "not-the-password-1" });
        var unknownUser = await client.PostAsJsonAsync("/api/auth/login", new { username = TestData.Username("ghost"), password = TestData.Password });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        var first = await wrongPassword.ReadAsync<ProblemBody>();
        var second = await unknownUser.ReadAsync<ProblemBody>();
        Assert.Equal("Wrong username or password", first.Title);
        Assert.Equal(first.Title, second.Title);
    }

    [Fact]
    public async Task Protected_routes_need_a_valid_token()
    {
        var client = api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task Login_attempts_are_rate_limited()
    {
        using var limited = api.Factory.WithWebHostBuilder(host => host.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:AuthPerMinute"] = "3" })));
        var client = limited.CreateClient();

        var codes = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "nobody_here", password = "whatever-123" });
            codes.Add(response.StatusCode);
        }

        Assert.Equal(
            new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests },
            codes);
    }

    [Fact]
    public async Task Health_checks_the_database()
    {
        var response = await api.Factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}

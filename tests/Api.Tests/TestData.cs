using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Transcendence.Api.Tests;

public sealed record TokenBody(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record ProfileBody(string Username, int Rating, int Wins, int Losses, int Rank);

public sealed record MatchBody(long Id, string Reporter, string Opponent, int ReporterScore, int OpponentScore,
    string Status, int? RatingChange, string Winner);

public sealed record ProblemBody(string? Title, int? Status);

public static class TestData
{
    public const string Password = "s3cret-pass42";

    /// <summary>A fresh username (16 characters) so tests never collide.</summary>
    public static string Username(string prefix) => (prefix + "_" + Guid.NewGuid().ToString("N"))[..16];

    /// <summary>Reads a JSON body; an empty body fails the test.</summary>
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) where T : class =>
        await response.Content.ReadFromJsonAsync<T>()
        ?? throw new InvalidOperationException($"{(int)response.StatusCode}: empty {typeof(T).Name} body");

    public static async Task<T> GetJsonAsync<T>(this HttpClient client, string url) where T : class =>
        await client.GetFromJsonAsync<T>(url) ?? throw new InvalidOperationException($"GET {url}: empty body");

    /// <summary>Registers a new player and returns a client that sends its token.</summary>
    public static async Task<HttpClient> LoggedInClientAsync(this WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var registered = await client.PostAsJsonAsync("/api/auth/register", new { username, password = Password });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = await login.ReadAsync<TokenBody>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
}

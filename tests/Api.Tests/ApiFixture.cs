using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Transcendence.Api.Tests;

/// <summary>
/// Starts the API once for every integration test, on a throwaway PostgreSQL database
/// that is created before the first test and dropped after the last one.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly string admin = Environment.GetEnvironmentVariable("TEST_POSTGRES")
        ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";

    private readonly string database = $"tapi_test_{Guid.NewGuid():N}";

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync($"CREATE DATABASE {database}");
        var connection = new NpgsqlConnectionStringBuilder(admin) { Database = database };
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", connection.ConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", "integration-tests-signing-key-0123456789abcdef");
        Environment.SetEnvironmentVariable("RateLimiting__AuthPerMinute", "100000");
        Factory = new WebApplicationFactory<Program>();
        using var warmUp = Factory.CreateClient(); // builds the host, which applies the migrations
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(admin);
        await conn.OpenAsync();
        await using var command = new NpgsqlCommand(sql, conn);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

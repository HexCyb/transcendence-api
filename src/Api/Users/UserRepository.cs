using Dapper;
using Npgsql;

namespace Transcendence.Api.Users;

/// <summary>A row of the users table (internal: the password hash never leaves the API).</summary>
public sealed class UserRecord
{
    public long Id { get; set; }

    public string Username { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public int Rating { get; set; }

    public int Wins { get; set; }

    public int Losses { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>What other players may see about a player.</summary>
public sealed class ProfileRow
{
    public string Username { get; set; } = "";

    public int Rating { get; set; }

    public int Wins { get; set; }

    public int Losses { get; set; }

    public int Rank { get; set; }

    public DateTime JoinedAt { get; set; }
}

public sealed class UserRepository(NpgsqlDataSource db)
{
    // rank() is computed over every player before filtering, so a single profile still knows its position.
    private const string RankedProfiles = """
        SELECT id, username, rating, wins, losses, created_at AS joined_at,
               CAST(rank() OVER (ORDER BY rating DESC) AS integer) AS rank
        FROM users
        """;

    /// <summary>Creates a player, or returns null when the username is taken (ignoring case).</summary>
    public async Task<UserRecord?> CreateAsync(string username, string passwordHash, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        try
        {
            return await conn.QuerySingleAsync<UserRecord>(new CommandDefinition(
                "INSERT INTO users (username, password_hash) VALUES (@username, @passwordHash) RETURNING *",
                new { username, passwordHash }, cancellationToken: ct));
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    public async Task<UserRecord?> FindByUsernameAsync(string username, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UserRecord>(new CommandDefinition(
            "SELECT * FROM users WHERE lower(username) = lower(@username)", new { username }, cancellationToken: ct));
    }

    public async Task<long?> IdOfAsync(string username, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT id FROM users WHERE lower(username) = lower(@username)", new { username }, cancellationToken: ct));
    }

    public async Task UpdatePasswordHashAsync(long id, string passwordHash, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE users SET password_hash = @passwordHash WHERE id = @id", new { id, passwordHash }, cancellationToken: ct));
    }

    public async Task<ProfileRow?> ProfileAsync(string username, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProfileRow>(new CommandDefinition(
            $"SELECT * FROM ({RankedProfiles}) ranked WHERE lower(username) = lower(@username)",
            new { username }, cancellationToken: ct));
    }

    public async Task<ProfileRow?> ProfileByIdAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProfileRow>(new CommandDefinition(
            $"SELECT * FROM ({RankedProfiles}) ranked WHERE id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ProfileRow>> LeaderboardAsync(int limit, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ProfileRow>(new CommandDefinition(
            $"{RankedProfiles} ORDER BY rating DESC, wins DESC, username LIMIT @limit", new { limit }, cancellationToken: ct));
        return rows.AsList();
    }
}

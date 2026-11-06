using Dapper;
using Npgsql;

namespace Transcendence.Api.Data;

/// <summary>
/// Applies the SQL files embedded from Data/Migrations, in name order, once each.
/// An advisory lock makes two instances starting at the same time wait for each other.
/// </summary>
public sealed class Migrator(NpgsqlDataSource db, ILogger<Migrator> log)
{
    private const long LockKey = 4242;

    public async Task RunAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition("SELECT pg_advisory_xact_lock(@LockKey)", new { LockKey }, tx, cancellationToken: ct));
        await conn.ExecuteAsync(new CommandDefinition(
            "CREATE TABLE IF NOT EXISTS schema_migrations (version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())",
            transaction: tx, cancellationToken: ct));

        var applied = (await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT version FROM schema_migrations", transaction: tx, cancellationToken: ct))).ToHashSet();

        var assembly = typeof(Migrator).Assembly;
        var scripts = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("migrations/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        foreach (var name in scripts)
        {
            var version = Path.GetFileNameWithoutExtension(name);
            if (applied.Contains(version))
            {
                continue;
            }
            await using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"missing embedded migration {name}");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(sql, transaction: tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO schema_migrations (version) VALUES (@version)", new { version }, tx, cancellationToken: ct));
            log.LogInformation("Applied migration {Version}", version);
        }
        await tx.CommitAsync(ct);
    }
}

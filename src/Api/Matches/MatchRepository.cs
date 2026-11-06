using Dapper;
using Npgsql;

namespace Transcendence.Api.Matches;

/// <summary>A match as the API shows it.</summary>
public sealed class MatchView
{
    public long Id { get; set; }

    public string Reporter { get; set; } = "";

    public string Opponent { get; set; } = "";

    public int ReporterScore { get; set; }

    public int OpponentScore { get; set; }

    public string Status { get; set; } = "";

    /// <summary>Elo points the winner took from the loser; null until the match is confirmed.</summary>
    public int? RatingChange { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    public string Winner => ReporterScore > OpponentScore ? Reporter : Opponent;
}

public enum Decision
{
    Done,
    NotFound,
    NotYours,
    AlreadyDecided,
}

internal sealed class MatchRow
{
    public long Id { get; set; }

    public long ReporterId { get; set; }

    public long OpponentId { get; set; }

    public int ReporterScore { get; set; }

    public int OpponentScore { get; set; }

    public string Status { get; set; } = "";
}

internal sealed class RatingRow
{
    public long Id { get; set; }

    public int Rating { get; set; }
}

public sealed class MatchRepository(NpgsqlDataSource db)
{
    private const string View = """
        SELECT m.id, r.username AS reporter, o.username AS opponent, m.reporter_score, m.opponent_score,
               m.status, m.rating_change, m.created_at, m.decided_at
        FROM matches m
        JOIN users r ON r.id = m.reporter_id
        JOIN users o ON o.id = m.opponent_id
        """;

    public async Task<MatchView> ReportAsync(long reporterId, long opponentId, int reporterScore, int opponentScore, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            INSERT INTO matches (reporter_id, opponent_id, reporter_score, opponent_score)
            VALUES (@reporterId, @opponentId, @reporterScore, @opponentScore)
            RETURNING id
            """,
            new { reporterId, opponentId, reporterScore, opponentScore }, cancellationToken: ct));
        return await FindAsync(conn, id, null, ct) ?? throw new InvalidOperationException($"match {id} vanished");
    }

    /// <summary>Matches reported against this player that are waiting for an answer.</summary>
    public async Task<IReadOnlyList<MatchView>> PendingForAsync(long opponentId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<MatchView>(new CommandDefinition(
            $"{View} WHERE m.opponent_id = @opponentId AND m.status = 'pending' ORDER BY m.created_at",
            new { opponentId }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Confirmed matches of a player, newest first.</summary>
    public async Task<IReadOnlyList<MatchView>> HistoryAsync(string username, int limit, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<MatchView>(new CommandDefinition(
            $"""
            {View}
            WHERE m.status = 'confirmed'
              AND (lower(r.username) = lower(@username) OR lower(o.username) = lower(@username))
            ORDER BY m.decided_at DESC
            LIMIT @limit
            """,
            new { username, limit }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// The opponent confirms (ratings and stats change) or rejects (nothing changes) a pending match.
    /// Everything runs in one transaction; the match row and both player rows are locked, the players
    /// always in id order, so two confirmations at the same time cannot deadlock or lose an update.
    /// </summary>
    public async Task<(Decision Decision, MatchView? Match)> DecideAsync(long matchId, long userId, bool confirm, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var match = await conn.QuerySingleOrDefaultAsync<MatchRow>(new CommandDefinition(
            "SELECT id, reporter_id, opponent_id, reporter_score, opponent_score, status FROM matches WHERE id = @matchId FOR UPDATE",
            new { matchId }, tx, cancellationToken: ct));
        if (match is null || (match.ReporterId != userId && match.OpponentId != userId))
        {
            return (Decision.NotFound, null); // other people's matches are invisible
        }
        if (match.OpponentId != userId)
        {
            return (Decision.NotYours, null);
        }
        if (match.Status != "pending")
        {
            return (Decision.AlreadyDecided, null);
        }

        if (!confirm)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE matches SET status = 'rejected', decided_at = now() WHERE id = @matchId", new { matchId }, tx, cancellationToken: ct));
        }
        else
        {
            var ratings = (await conn.QueryAsync<RatingRow>(new CommandDefinition(
                    "SELECT id, rating FROM users WHERE id IN (@a, @b) ORDER BY id FOR UPDATE",
                    new { a = match.ReporterId, b = match.OpponentId }, tx, cancellationToken: ct)))
                .ToDictionary(row => row.Id, row => row.Rating);

            var (winner, loser) = match.ReporterScore > match.OpponentScore
                ? (match.ReporterId, match.OpponentId)
                : (match.OpponentId, match.ReporterId);
            var delta = Elo.Delta(ratings[winner], ratings[loser]);

            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE users SET rating = rating + @delta, wins = wins + 1 WHERE id = @winner", new { delta, winner }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE users SET rating = rating - @delta, losses = losses + 1 WHERE id = @loser", new { delta, loser }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE matches SET status = 'confirmed', rating_change = @delta, decided_at = now() WHERE id = @matchId",
                new { delta, matchId }, tx, cancellationToken: ct));
        }

        var view = await FindAsync(conn, matchId, tx, ct);
        await tx.CommitAsync(ct);
        return (Decision.Done, view);
    }

    private static async Task<MatchView?> FindAsync(NpgsqlConnection conn, long id, NpgsqlTransaction? tx, CancellationToken ct) =>
        await conn.QuerySingleOrDefaultAsync<MatchView>(new CommandDefinition($"{View} WHERE m.id = @id", new { id }, tx, cancellationToken: ct));
}

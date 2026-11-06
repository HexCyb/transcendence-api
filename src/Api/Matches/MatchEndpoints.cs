using System.Security.Claims;
using Transcendence.Api.Auth;
using Transcendence.Api.Users;

namespace Transcendence.Api.Matches;

public static class MatchEndpoints
{
    public const int MaxScore = 21;

    public sealed record MatchReport(string? Opponent, int MyScore, int OpponentScore);

    public static IEndpointRouteBuilder MapMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var matches = app.MapGroup("/api/matches").RequireAuthorization().WithTags("Matches");
        matches.MapPost("", Report);
        matches.MapGet("/pending", Pending);
        matches.MapPost("/{id:long}/confirm", Confirm);
        matches.MapPost("/{id:long}/reject", Reject);
        return app;
    }

    private static async Task<IResult> Report(
        MatchReport body, ClaimsPrincipal user, UserRepository users, MatchRepository matches, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body.Opponent))
        {
            errors["opponent"] = ["the opponent's username is required"];
        }
        if (body.MyScore is < 0 or > MaxScore || body.OpponentScore is < 0 or > MaxScore)
        {
            errors["score"] = [$"scores go from 0 to {MaxScore}"];
        }
        else if (body.MyScore == body.OpponentScore)
        {
            errors["score"] = ["a Pong match cannot end in a draw"];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var me = user.UserId();
        var opponent = await users.IdOfAsync(body.Opponent!, ct);
        if (opponent is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "No player has this username");
        }
        if (opponent == me)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["opponent"] = ["you cannot play against yourself"] });
        }
        var match = await matches.ReportAsync(me, opponent.Value, body.MyScore, body.OpponentScore, ct);
        return TypedResults.Created($"/api/matches/{match.Id}", match);
    }

    private static async Task<IResult> Pending(ClaimsPrincipal user, MatchRepository matches, CancellationToken ct) =>
        TypedResults.Ok(await matches.PendingForAsync(user.UserId(), ct));

    private static async Task<IResult> Confirm(long id, ClaimsPrincipal user, MatchRepository matches, CancellationToken ct) =>
        ToResult(await matches.DecideAsync(id, user.UserId(), confirm: true, ct));

    private static async Task<IResult> Reject(long id, ClaimsPrincipal user, MatchRepository matches, CancellationToken ct) =>
        ToResult(await matches.DecideAsync(id, user.UserId(), confirm: false, ct));

    private static IResult ToResult((Decision Decision, MatchView? Match) outcome) => outcome.Decision switch
    {
        Decision.Done => TypedResults.Ok(outcome.Match),
        Decision.NotFound => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "No such match"),
        Decision.NotYours => TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
            title: "Only the opponent can confirm or reject a match"),
        _ => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "This match was already decided"),
    };
}

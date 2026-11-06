using System.Security.Claims;
using Transcendence.Api.Auth;
using Transcendence.Api.Matches;

namespace Transcendence.Api.Users;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Players");
        api.MapGet("/users/me", Me).RequireAuthorization();
        api.MapGet("/users/{username}", Profile);
        api.MapGet("/users/{username}/matches", History);
        api.MapGet("/leaderboard", Leaderboard);
        return app;
    }

    private static async Task<IResult> Me(ClaimsPrincipal user, UserRepository users, CancellationToken ct)
    {
        var profile = await users.ProfileByIdAsync(user.UserId(), ct);
        return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
    }

    private static async Task<IResult> Profile(string username, UserRepository users, CancellationToken ct)
    {
        var profile = await users.ProfileAsync(username, ct);
        return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
    }

    private static async Task<IResult> History(
        string username, int? limit, UserRepository users, MatchRepository matches, CancellationToken ct)
    {
        if (await users.IdOfAsync(username, ct) is null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(await matches.HistoryAsync(username, Clamp(limit), ct));
    }

    private static async Task<IResult> Leaderboard(int? limit, UserRepository users, CancellationToken ct) =>
        TypedResults.Ok(await users.LeaderboardAsync(Clamp(limit), ct));

    private static int Clamp(int? limit) => Math.Clamp(limit ?? 20, 1, 100);
}

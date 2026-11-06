using System.Net;
using System.Net.Http.Json;

namespace Transcendence.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class MatchTests(ApiFixture api)
{
    [Fact]
    public async Task A_confirmed_match_moves_ratings_by_elo_and_shows_in_history()
    {
        var (alice, aliceName) = await Player("alice");
        var (bob, bobName) = await Player("bob");

        var match = await Report(alice, bobName, 5, 3);
        Assert.Equal("pending", match.Status);
        Assert.Empty(await History(alice, aliceName));                       // nothing counts yet
        Assert.Contains(await Pending(bob), pending => pending.Id == match.Id);

        var confirm = await bob.PostAsync($"/api/matches/{match.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var decided = await confirm.ReadAsync<MatchBody>();

        Assert.Equal("confirmed", decided.Status);
        Assert.Equal(aliceName, decided.Winner);
        Assert.Equal(16, decided.RatingChange);                               // equal ratings: K / 2

        var a = await Profile(bob, aliceName);
        var b = await Profile(bob, bobName);
        Assert.Equal((1016, 1, 0), (a.Rating, a.Wins, a.Losses));
        Assert.Equal((984, 0, 1), (b.Rating, b.Wins, b.Losses));
        Assert.Single(await History(alice, aliceName));
        Assert.DoesNotContain(await Pending(bob), pending => pending.Id == match.Id);
    }

    [Fact]
    public async Task The_leaderboard_puts_the_winner_above_the_loser()
    {
        var (alice, aliceName) = await Player("alice");
        var (bob, bobName) = await Player("bob");
        var match = await Report(alice, bobName, 2, 5);                       // bob wins
        Assert.Equal(HttpStatusCode.OK, (await bob.PostAsync($"/api/matches/{match.Id}/confirm", null)).StatusCode);

        var board = await alice.GetJsonAsync<List<ProfileBody>>("/api/leaderboard?limit=100");

        var winner = board.FindIndex(p => p.Username == bobName);
        var loser = board.FindIndex(p => p.Username == aliceName);
        Assert.NotEqual(-1, winner);
        Assert.True(loser > winner, $"the winner is at {winner}, the loser at {loser}");
        // rank() = 1 + the number of players with a strictly higher rating
        Assert.Equal(1 + board.Count(p => p.Rating > board[winner].Rating), board[winner].Rank);
    }

    [Fact]
    public async Task Only_the_opponent_can_decide_and_only_once()
    {
        var (alice, _) = await Player("alice");
        var (bob, bobName) = await Player("bob");
        var (eve, _) = await Player("eve");
        var match = await Report(alice, bobName, 7, 4);

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PostAsync($"/api/matches/{match.Id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await eve.PostAsync($"/api/matches/{match.Id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.PostAsync($"/api/matches/{match.Id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await bob.PostAsync($"/api/matches/{match.Id}/reject", null)).StatusCode);
    }

    [Fact]
    public async Task A_rejected_match_changes_nothing()
    {
        var (alice, aliceName) = await Player("alice");
        var (bob, bobName) = await Player("bob");
        var match = await Report(alice, bobName, 5, 0);

        var reject = await bob.PostAsync($"/api/matches/{match.Id}/reject", null);
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        Assert.Equal("rejected", (await reject.ReadAsync<MatchBody>()).Status);
        var a = await Profile(bob, aliceName);
        Assert.Equal((1000, 0, 0), (a.Rating, a.Wins, a.Losses));
        Assert.Empty(await History(alice, aliceName));
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(22, 1)]
    [InlineData(-1, 5)]
    public async Task Impossible_scores_are_refused(int mine, int theirs)
    {
        var (alice, _) = await Player("alice");
        var (_, bobName) = await Player("bob");

        var response = await alice.PostAsJsonAsync("/api/matches", new { opponent = bobName, myScore = mine, opponentScore = theirs });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_opponents_and_yourself_are_refused()
    {
        var (alice, aliceName) = await Player("alice");

        var unknown = await alice.PostAsJsonAsync("/api/matches", new { opponent = TestData.Username("ghost"), myScore = 5, opponentScore = 1 });
        var self = await alice.PostAsJsonAsync("/api/matches", new { opponent = aliceName, myScore = 5, opponentScore = 1 });

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
    }

    private async Task<(HttpClient Client, string Name)> Player(string prefix)
    {
        var name = TestData.Username(prefix);
        return (await api.Factory.LoggedInClientAsync(name), name);
    }

    private static async Task<MatchBody> Report(HttpClient client, string opponent, int myScore, int opponentScore)
    {
        var response = await client.PostAsJsonAsync("/api/matches", new { opponent, myScore, opponentScore });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<MatchBody>();
    }

    private static Task<List<MatchBody>> History(HttpClient client, string username) =>
        client.GetJsonAsync<List<MatchBody>>($"/api/users/{username}/matches");

    private static Task<List<MatchBody>> Pending(HttpClient client) =>
        client.GetJsonAsync<List<MatchBody>>("/api/matches/pending");

    private static Task<ProfileBody> Profile(HttpClient client, string username) =>
        client.GetJsonAsync<ProfileBody>($"/api/users/{username}");
}

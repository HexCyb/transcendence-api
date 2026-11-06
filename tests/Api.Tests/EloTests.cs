using Transcendence.Api.Matches;

namespace Transcendence.Api.Tests;

public sealed class EloTests
{
    [Fact]
    public void Equal_ratings_exchange_half_of_k() => Assert.Equal(16, Elo.Delta(1000, 1000));

    [Fact]
    public void A_favourite_wins_few_points() => Assert.Equal(3, Elo.Delta(1400, 1000));

    [Fact]
    public void An_upset_wins_many_points() => Assert.Equal(29, Elo.Delta(1000, 1400));

    [Theory]
    [InlineData(100, 3000)]
    [InlineData(3000, 100)]
    [InlineData(1500, 1499)]
    public void The_change_always_stays_between_1_and_k(int winner, int loser) =>
        Assert.InRange(Elo.Delta(winner, loser), 1, Elo.K);

    [Fact]
    public void Expected_scores_of_both_players_add_up_to_one() =>
        Assert.Equal(1.0, Elo.Expected(1234, 987) + Elo.Expected(987, 1234), precision: 10);
}

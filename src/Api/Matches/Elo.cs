namespace Transcendence.Api.Matches;

/// <summary>
/// Elo rating. The winner takes <see cref="Delta"/> points from the loser, so the total never changes.
/// Beating a stronger player is worth more than beating a weaker one.
/// </summary>
public static class Elo
{
    public const int K = 32;

    /// <summary>Probability that a player rated <paramref name="rating"/> beats one rated <paramref name="opponent"/>.</summary>
    public static double Expected(int rating, int opponent) => 1.0 / (1.0 + Math.Pow(10, (opponent - rating) / 400.0));

    /// <summary>Points the winner gains (and the loser loses): between 1 and K.</summary>
    public static int Delta(int winnerRating, int loserRating) =>
        Math.Max(1, (int)Math.Round(K * (1 - Expected(winnerRating, loserRating)), MidpointRounding.AwayFromZero));
}

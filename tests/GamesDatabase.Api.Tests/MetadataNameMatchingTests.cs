using GamesDatabase.Api.Common;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class MetadataNameMatchingTests
{
    [Theory]
    [InlineData("Control™", "CONTROL")]
    [InlineData("Assassin’s Creed®", "Assassin's Creed")]
    [InlineData("Pokémon:  Legends — Test", "Pokemon Legends Test")]
    [InlineData("Game - Deluxe Edition", "Game: Deluxe Edition")]
    public void Unique_cosmetic_match_is_reliable_without_global_search_changes(string input, string candidate)
    {
        Assert.Equal(candidate, MetadataNameMatching.Best(new[] { candidate }, input, value => value));
    }
    [Theory]
    [InlineData("Game", "Game 2")]
    [InlineData("Game", "Game Remastered")]
    [InlineData("Game", "Game Definitive Edition")]
    [InlineData("Game 2", "Game 3")]
    [InlineData("Game", "Game Soundtrack")]
    [InlineData("Game", "Game DLC")]
    [InlineData("Game Part I", "Game Part II")]
    [InlineData("Game 2020", "Game 2024")]
    public void A_nearest_title_is_not_permission_to_choose_another_game_or_edition(string input, string candidate)
    {
        Assert.Null(MetadataNameMatching.Best(new[] { candidate }, input, value => value));
    }
    [Fact]
    public void Tied_cosmetic_matches_remain_ambiguous_but_an_exact_match_wins()
    {
        Assert.Null(MetadataNameMatching.Best(new[] { "Game™", "Game®" }, "Game", value => value));
        Assert.Equal("Game", MetadataNameMatching.Best(new[] { "Game", "Game™" }, "Game", value => value));
    }
}

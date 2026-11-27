using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
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

    [Fact]
    public void A_verified_shared_Steam_ID_is_stronger_than_a_ports_different_release_year_but_different_IDs_conflict()
    {
        var console = new GameMetadataIdentityDto { SteamAppId = 100, ReleaseYear = 2022, Platforms = ["PS5", "PC"] };
        Assert.False(MetadataEditionMatching.Conflicts(console, new() { SteamAppId = 100, ReleaseYear = 2023, Platforms = ["PC"] }));
        Assert.True(MetadataEditionMatching.Conflicts(console, new() { SteamAppId = 200, ReleaseYear = 2022, Platforms = ["PC"] }));
        Assert.True(MetadataEditionMatching.Conflicts(new() { ReleaseYear = 1998 }, new() { ReleaseYear = 2026 }));
    }
}

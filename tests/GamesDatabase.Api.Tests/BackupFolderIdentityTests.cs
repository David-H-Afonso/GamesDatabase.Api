using GamesDatabase.Api.Common;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class BackupFolderIdentityTests
{
    [Fact]
    public void Unique_titles_keep_their_existing_paths()
    {
        var folders = FolderNameHelper.BuildExportFolderNames([(1, "A Short Hike"), (2, "Outer Wilds")], "Unknown_Game");
        Assert.Equal("A_Short_Hike", folders[1]); Assert.Equal("Outer_Wilds", folders[2]);
    }

    [Fact]
    public void Same_title_editions_get_distinct_deterministic_ID_folders()
    {
        (int Id, string Name)[] games = [(7, "Same title"), (3, "Same title")];
        var folders = FolderNameHelper.BuildExportFolderNames(games, "Unknown_Game");
        Assert.Equal("Same_title__gdb_7", folders[7]); Assert.Equal("Same_title__gdb_3", folders[3]);
        Assert.Equal(folders.OrderBy(item => item.Key), FolderNameHelper.BuildExportFolderNames(games.Reverse(), "Unknown_Game").OrderBy(item => item.Key));
    }

    [Fact]
    public void Case_punctuation_and_accent_collisions_never_share_a_folder()
    {
        var folders = FolderNameHelper.BuildExportFolderNames([(1, "Pokémon!"), (2, "Pokemon"), (3, "POKEMON")], "Unknown_Game");
        Assert.Equal(3, folders.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count()); Assert.All(folders, item => Assert.Contains($"__gdb_{item.Key}", item.Value));
    }

    [Fact]
    public void Literal_title_paths_stay_unchanged_when_other_titles_need_ID_suffixes()
    {
        var folders = FolderNameHelper.BuildExportFolderNames([(1, "Title"), (2, "Title"), (3, "Title__gdb_1")], "Unknown_Game");
        // MakeSafeFolderName collapses repeated underscores in literal titles.
        Assert.Equal(3, folders.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count()); Assert.Equal("Title_gdb_1", folders[3]);
    }

    [Fact]
    public void Long_and_empty_titles_stay_bounded_and_distinct()
    {
        var longName = new string('A', 240);
        var folders = FolderNameHelper.BuildExportFolderNames([(1, longName), (2, longName + "B"), (3, "..."), (4, "???")], "Unknown_Playlist");
        Assert.Equal(4, folders.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(folders.Values, folder => { Assert.InRange(folder.Length, 1, 200); Assert.Equal(folder, Path.GetFileName(folder)); });
        Assert.StartsWith("Unknown_Playlist__gdb_", folders[3]); Assert.StartsWith("Unknown_Playlist__gdb_", folders[4]);
    }
}

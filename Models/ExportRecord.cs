using CsvHelper.Configuration.Attributes;

namespace GamesDatabase.Api.Models;

public class ExportRecord
{
    [Name("Type")]
    public string Type { get; set; } = string.Empty;

    [Name("Name")]
    public string Name { get; set; } = string.Empty;

    [Name("Color")]
    public string? Color { get; set; }

    [Name("IsActive")]
    public string? IsActive { get; set; }

    [Name("SortOrder")]
    public string? SortOrder { get; set; }

    [Name("IsDefault")]
    public string? IsDefault { get; set; }

    [Name("StatusType")]
    public string? StatusType { get; set; }

    [Name("Status")]
    public string? Status { get; set; }

    [Name("Platform")]
    public string? Platform { get; set; }

    [Name("PlayWith")]
    public string? PlayWith { get; set; }

    [Name("PlayedStatus")]
    public string? PlayedStatus { get; set; }

    [Name("Released")]
    public string? Released { get; set; }

    [Name("Started")]
    public string? Started { get; set; }

    [Name("Finished")]
    public string? Finished { get; set; }

    [Name("Score")]
    public string? Score { get; set; }

    [Name("Critic")]
    public string? Critic { get; set; }

    [Name("CriticProvider")]
    public string? CriticProvider { get; set; }

    [Name("Grade")]
    public string? Grade { get; set; }

    [Name("Completion")]
    public string? Completion { get; set; }

    [Name("Story")]
    public string? Story { get; set; }

    [Name("Comment")]
    public string? Comment { get; set; }

    [Name("Logo")]
    public string? Logo { get; set; }

    [Name("Cover")]
    public string? Cover { get; set; }

    [Name("Description")]
    public string? Description { get; set; }

    [Name("FiltersJson")]
    public string? FiltersJson { get; set; }

    [Name("SortingJson")]
    public string? SortingJson { get; set; }

    [Name("IsPublic")]
    public string? IsPublic { get; set; }

    [Name("CreatedBy")]
    public string? CreatedBy { get; set; }
}

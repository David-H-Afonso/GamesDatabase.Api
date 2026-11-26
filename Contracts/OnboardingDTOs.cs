namespace GamesDatabase.Api.Contracts;

public sealed class OnboardingStateDto
{
    public string Version { get; set; } = "3.0.0";
    public int Revision { get; set; }
    public bool Enabled { get; set; }
    public bool RequiresPassword { get; set; }
    public bool HasSamples { get; set; }
    public string Step { get; set; } = "welcome";
    public string? CompletedVersion { get; set; }
    public List<GameDto> SampleGames { get; set; } = [];
    public int SamplePlaylists { get; set; }
    public List<TutorialSamplePlaylistDto> SampleLists { get; set; } = [];
}

public sealed class TutorialSamplePlaylistDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class OnboardingProgressRequest
{
    public int Revision { get; set; }
    public string Step { get; set; } = "welcome";
    public bool Enabled { get; set; } = true;
    public bool Complete { get; set; }
}

public sealed class OnboardingStartRequest
{
    public int Revision { get; set; }
    public bool Restart { get; set; }
}

public sealed class CreateTutorialSamplesRequest
{
    public int Revision { get; set; }
    public bool Confirm { get; set; }
}

public sealed class ExpectedSampleGameDto
{
    public int Id { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ChangeTutorialSamplesRequest
{
    public int Revision { get; set; }
    public bool Confirm { get; set; }
    public bool RemoveEditedSamples { get; set; }
    public List<ExpectedSampleGameDto> ExpectedGames { get; set; } = [];
    public List<ExpectedSampleGameDto> ExpectedPlaylists { get; set; } = [];
}

public sealed class TutorialSampleChangeDto
{
    public int RemovedGames { get; set; }
    public int KeptGames { get; set; }
    public int RemovedPlaylists { get; set; }
    public int KeptPlaylists { get; set; }
    public OnboardingStateDto State { get; set; } = new();
}

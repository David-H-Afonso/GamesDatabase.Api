namespace GamesDatabase.Api.Common;

/// <summary>A provider failure is not an authoritative empty search result.</summary>
public sealed class MetadataSourceUnavailableException(string provider, int? retryAfterSeconds = null)
    : HttpRequestException($"The public {provider} metadata source is unavailable.")
{
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

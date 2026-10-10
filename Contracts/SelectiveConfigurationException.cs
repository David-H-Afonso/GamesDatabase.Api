namespace GamesDatabase.Api.Contracts;

/// <summary>A client-supplied selective import/export configuration is invalid.</summary>
public sealed class SelectiveConfigurationException(string message) : ArgumentException(message);

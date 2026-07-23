using System.ComponentModel.DataAnnotations;

namespace GamesDatabase.Api.Contracts;

public sealed class HouseholdAuthorizeRequest
{
    [Required, MaxLength(100)]
    public string ClientId { get; set; } = string.Empty;

    [Required, MaxLength(2048)]
    public string RedirectUri { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string State { get; set; } = string.Empty;

    [Required, MaxLength(128)]
    public string CodeChallenge { get; set; } = string.Empty;

    [Required, MaxLength(10)]
    public string CodeChallengeMethod { get; set; } = string.Empty;

    [Required]
    public List<string> Scopes { get; set; } = new();

    public bool Approved { get; set; } = true;
}

public sealed class HouseholdAuthorizeResponse
{
    public string RedirectUrl { get; set; } = string.Empty;
}

public sealed class HouseholdTokenRequest
{
    [Required, MaxLength(50)]
    public string GrantType { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ClientId { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string? RedirectUri { get; set; }

    [MaxLength(512)]
    public string? Code { get; set; }

    [MaxLength(128)]
    public string? CodeVerifier { get; set; }

    [MaxLength(512)]
    public string? RefreshToken { get; set; }
}

public sealed class HouseholdTokenResponse
{
    public string TokenType { get; set; } = "Bearer";
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public int RefreshExpiresIn { get; set; }
    public string Scope { get; set; } = string.Empty;
    public Guid ConnectionId { get; set; }
    public HouseholdAccountDto Account { get; set; } = new();
}

public sealed class HouseholdAccountDto
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class HouseholdMeResponse
{
    public Guid ConnectionId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public HouseholdAccountDto Account { get; set; } = new();
}

public sealed class HouseholdRevokeRequest
{
    [Required, MaxLength(512)]
    public string Token { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? TokenTypeHint { get; set; }
}

public sealed class OAuthErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string? ErrorDescription { get; set; }
}

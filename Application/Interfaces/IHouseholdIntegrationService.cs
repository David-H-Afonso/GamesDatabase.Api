using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IHouseholdIntegrationService
{
    Task<HouseholdAuthorizeResult> AuthorizeAsync(int userId, HouseholdAuthorizeRequest request);
    Task<HouseholdTokenResult> ExchangeTokenAsync(HouseholdTokenRequest request);
    Task RevokeAsync(string token);
}

public sealed record HouseholdAuthorizeResult(
    bool Success,
    string? RedirectUrl = null,
    string? Error = null,
    string? ErrorDescription = null,
    bool CanRedirect = false);

public sealed record HouseholdTokenResult(
    bool Success,
    HouseholdTokenResponse? Response = null,
    string? Error = null,
    string? ErrorDescription = null);

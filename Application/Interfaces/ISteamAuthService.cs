namespace GamesDatabase.Api.Application.Interfaces;

public interface ISteamAuthService
{
    string BuildLoginUrl(Guid nonce, string callbackUrl);
    Task<string?> ValidateCallbackAsync(IQueryCollection queryParams);
    Guid StoreNonce(int? userId, string mode);
    (int? UserId, string Mode)? ConsumeNonce(Guid nonce);
}

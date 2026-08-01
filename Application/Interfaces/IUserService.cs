using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IUserService
{
    Task<List<UserDto>> GetUsersAsync(int currentUserId);
    Task<CatalogServiceResult<UserDto>> GetUserByIdAsync(int id, int currentUserId);
    Task<CatalogServiceResult<UserDto>> CreateUserAsync(CreateUserRequest request, int currentUserId);
    Task<CatalogServiceResult> UpdateUserAsync(int id, UpdateUserRequest request, int currentUserId);
    Task<CatalogServiceResult> DeleteUserAsync(int id, int currentUserId);
    Task<CatalogServiceResult> ChangePasswordAsync(int id, ChangePasswordRequest request, int currentUserId);
    Task<bool> HealthCheckAsync();

    /// <summary>
    /// Returns whether the app is still in initial-setup state, i.e. the seeded
    /// default admin exists and has no password, so the login screen may safely
    /// offer the passwordless bootstrap sign-in. Never exposes real usernames.
    /// </summary>
    Task<SetupStatusDto> GetSetupStatusAsync();
}

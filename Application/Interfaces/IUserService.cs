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
}

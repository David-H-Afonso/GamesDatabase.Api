using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IOnboardingService
{
    Task<OnboardingStateDto> GetAsync(int userId, CancellationToken cancellationToken = default);
    Task<CatalogServiceResult<OnboardingStateDto>> StartAsync(int userId, OnboardingStartRequest request, CancellationToken cancellationToken = default);
    Task<CatalogServiceResult<OnboardingStateDto>> SaveAsync(int userId, OnboardingProgressRequest request, CancellationToken cancellationToken = default);
    Task<CatalogServiceResult<OnboardingStateDto>> CreateSamplesAsync(int userId, CreateTutorialSamplesRequest request, CancellationToken cancellationToken = default);
    Task<CatalogServiceResult<TutorialSampleChangeDto>> ChangeSamplesAsync(int userId, ChangeTutorialSamplesRequest request, bool remove, CancellationToken cancellationToken = default);
}

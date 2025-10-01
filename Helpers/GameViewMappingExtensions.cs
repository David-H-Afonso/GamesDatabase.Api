using GamesDatabase.Api.Models;
using GamesDatabase.Api.DTOs;
using System.Text.Json;

namespace GamesDatabase.Api.Helpers;

public static class GameViewMappingExtensions
{
    public static GameViewDto ToDto(this GameView view)
    {
        var configuration = new ViewConfiguration();

        try
        {
            if (!string.IsNullOrEmpty(view.FiltersJson))
            {
                // Try to deserialize as full configuration first (new format)
                try
                {
                    var full = JsonSerializer.Deserialize<ViewConfiguration>(view.FiltersJson);
                    if (full != null && full.FilterGroups.Any())
                    {
                        configuration = full;
                    }
                    else
                    {
                        var filters = JsonSerializer.Deserialize<List<ViewFilter>>(view.FiltersJson);
                        if (filters != null)
                        {
                            configuration.FilterGroups.Add(new FilterGroup
                            {
                                Filters = filters,
                                CombineWith = FilterLogic.And
                            });
                        }
                    }
                }
                catch (JsonException)
                {
                    var filters = JsonSerializer.Deserialize<List<ViewFilter>>(view.FiltersJson);
                    if (filters != null)
                    {
                        configuration.FilterGroups.Add(new FilterGroup
                        {
                            Filters = filters,
                            CombineWith = FilterLogic.And
                        });
                    }
                }
            }

            // If SortingJson exists (legacy), merge it
            if (!string.IsNullOrEmpty(view.SortingJson))
            {
                var sorting = JsonSerializer.Deserialize<List<ViewSort>>(view.SortingJson);
                if (sorting != null)
                {
                    configuration.Sorting = sorting;
                }
            }
        }
        catch (JsonException)
        {
            // If deserialization fails, return empty configuration
            configuration = new ViewConfiguration();
        }
        return new GameViewDto
        {
            Id = view.Id,
            Name = view.Name,
            Description = view.Description,
            Configuration = configuration,
            IsPublic = view.IsPublic,
            CreatedBy = view.CreatedBy,
            CreatedAt = view.CreatedAt,
            UpdatedAt = view.UpdatedAt
        };
    }

    public static GameViewSummaryDto ToSummaryDto(this GameView view)
    {
        var configuration = new ViewConfiguration();
        var sortCount = 0;
        try
        {
            if (!string.IsNullOrEmpty(view.FiltersJson))
            {
                try
                {
                    var full = JsonSerializer.Deserialize<ViewConfiguration>(view.FiltersJson);
                    if (full != null && full.FilterGroups.Any())
                    {
                        configuration = full;
                    }
                    else
                    {
                        var filters = JsonSerializer.Deserialize<List<ViewFilter>>(view.FiltersJson);
                        if (filters != null)
                        {
                            configuration.FilterGroups.Add(new FilterGroup
                            {
                                Filters = filters,
                                CombineWith = FilterLogic.And
                            });
                        }
                    }
                }
                catch (JsonException)
                {
                    var filters = JsonSerializer.Deserialize<List<ViewFilter>>(view.FiltersJson);
                    if (filters != null)
                    {
                        configuration.FilterGroups.Add(new FilterGroup
                        {
                            Filters = filters,
                            CombineWith = FilterLogic.And
                        });
                    }
                }
            }

            if (!string.IsNullOrEmpty(view.SortingJson))
            {
                var sorting = JsonSerializer.Deserialize<List<ViewSort>>(view.SortingJson);
                sortCount = sorting?.Count ?? 0;
            }
        }
        catch (JsonException)
        {
            configuration = new ViewConfiguration();
            sortCount = 0;
        }

        var totalFilterCount = configuration.FilterGroups.Sum(g => g.Filters.Count);

        return new GameViewSummaryDto
        {
            Id = view.Id,
            Name = view.Name,
            Description = view.Description,
            IsPublic = view.IsPublic,
            CreatedBy = view.CreatedBy,
            CreatedAt = view.CreatedAt,
            UpdatedAt = view.UpdatedAt,
            FilterCount = totalFilterCount,
            SortCount = sortCount
        };
    }

    public static GameView ToEntity(this GameViewCreateDto dto)
    {
        // Store the full configuration into FiltersJson to include CombineWith and Sorting.
        var fullConfigJson = JsonSerializer.Serialize(dto.Configuration);

        return new GameView
        {
            Name = dto.Name,
            Description = dto.Description,
            FiltersJson = fullConfigJson,
            SortingJson = null,
            IsPublic = dto.IsPublic,
            CreatedBy = dto.CreatedBy
        };
    }

    public static void UpdateFromDto(this GameView view, GameViewUpdateDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.Name))
            view.Name = dto.Name;

        if (dto.Description != null)
            view.Description = dto.Description;

        if (dto.Configuration != null)
        {
            // Store full configuration for future compatibility (includes CombineWith)
            view.FiltersJson = JsonSerializer.Serialize(dto.Configuration);
            view.SortingJson = null;
        }

        if (dto.IsPublic.HasValue)
            view.IsPublic = dto.IsPublic.Value;
    }
}
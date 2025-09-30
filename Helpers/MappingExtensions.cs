using GamesDatabase.Api.Models;
using GamesDatabase.Api.DTOs;

namespace GamesDatabase.Api.Helpers;

public static class MappingExtensions
{
    public static GameDto ToDto(this Game game)
    {
        return new GameDto
        {
            Id = game.Id,
            StatusId = game.StatusId,
            Name = game.Name,
            Grade = game.Grade,
            Critic = game.Critic,
            Story = game.Story,
            Completion = game.Completion,
            Score = game.Score,
            PlatformId = game.PlatformId,
            Released = game.Released,
            Started = game.Started,
            Finished = game.Finished,
            Comment = game.Comment,
            PlayWithId = game.PlayWithId,
            PlayedStatusId = game.PlayedStatusId,
            Logo = game.Logo,
            Cover = game.Cover,
            StatusName = game.Status?.Name,
            PlatformName = game.Platform?.Name,
            PlayWithName = game.PlayWith?.Name,
            PlayedStatusName = game.PlayedStatus?.Name
        };
    }

    public static Game ToEntity(this GameCreateDto dto)
    {
        var game = new Game
        {
            StatusId = dto.StatusId,
            Name = dto.Name,
            Grade = dto.Grade,
            Critic = dto.Critic,
            Story = dto.Story,
            Completion = dto.Completion,
            // Score se calcula automáticamente
            PlatformId = dto.PlatformId,
            Released = dto.Released,
            Started = dto.Started,
            Finished = dto.Finished,
            Comment = dto.Comment,
            PlayWithId = dto.PlayWithId,
            PlayedStatusId = dto.PlayedStatusId,
            Logo = dto.Logo,
            Cover = dto.Cover
        };

        // Calcular el score automáticamente
        game.CalculateScore();

        return game;
    }

    public static void UpdateEntity(this Game entity, GameUpdateDto dto)
    {
        if (dto.StatusId.HasValue) entity.StatusId = dto.StatusId.Value;
        if (!string.IsNullOrEmpty(dto.Name)) entity.Name = dto.Name;
        if (dto.Grade.HasValue) entity.Grade = dto.Grade.Value;
        if (dto.Critic.HasValue) entity.Critic = dto.Critic.Value;
        if (dto.Story.HasValue) entity.Story = dto.Story.Value;
        if (dto.Completion.HasValue) entity.Completion = dto.Completion.Value;
        if (dto.PlatformId.HasValue) entity.PlatformId = dto.PlatformId.Value;
        if (dto.Released != null) entity.Released = dto.Released;
        if (dto.Started != null) entity.Started = dto.Started;
        if (dto.Finished != null) entity.Finished = dto.Finished;
        if (dto.Comment != null) entity.Comment = dto.Comment;
        if (dto.PlayWithId.HasValue) entity.PlayWithId = dto.PlayWithId.Value;
        if (dto.PlayedStatusId.HasValue) entity.PlayedStatusId = dto.PlayedStatusId.Value;
        if (dto.Logo != null) entity.Logo = dto.Logo;
        if (dto.Cover != null) entity.Cover = dto.Cover;

        entity.CalculateScore();
    }

    public static GamePlatformDto ToDto(this GamePlatform platform)
    {
        return new GamePlatformDto
        {
            Id = platform.Id,
            Name = platform.Name,
            IsActive = platform.IsActive,
            Color = platform.Color
        };
    }

    public static GameStatusDto ToDto(this GameStatus status)
    {
        return new GameStatusDto
        {
            Id = status.Id,
            Name = status.Name,
            SortOrder = status.SortOrder,
            IsActive = status.IsActive,
            Color = status.Color,
            IsDefault = status.IsDefault,
            StatusType = status.StatusType.ToString(),
            IsSpecialStatus = status.IsSpecialStatus
        };
    }

    public static SpecialStatusDto ToSpecialStatusDto(this GameStatus status)
    {
        return new SpecialStatusDto
        {
            Id = status.Id,
            Name = status.Name,
            StatusType = status.StatusType.ToString(),
            IsDefault = status.IsDefault,
            Color = status.Color
        };
    }

    public static GamePlayWithDto ToDto(this GamePlayWith playWith)
    {
        return new GamePlayWithDto
        {
            Id = playWith.Id,
            Name = playWith.Name,
            IsActive = playWith.IsActive,
            Color = playWith.Color
        };
    }

    public static GamePlayedStatusDto ToDto(this GamePlayedStatus playedStatus)
    {
        return new GamePlayedStatusDto
        {
            Id = playedStatus.Id,
            Name = playedStatus.Name,
            IsActive = playedStatus.IsActive,
            Color = playedStatus.Color
        };
    }
}
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Application.Services;

public class GameReplayService : IGameReplayService
{
    private readonly GamesDbContext _context;

    public GameReplayService(GamesDbContext context)
    {
        _context = context;
    }

    public async Task<List<GameReplayDto>?> GetReplaysForGameAsync(int userId, int gameId)
    {
        var gameExists = await _context.Games.AnyAsync(g => g.Id == gameId && g.UserId == userId);
        if (!gameExists) return null;

        var replays = await _context.GameReplays
            .Where(r => r.GameId == gameId && r.UserId == userId)
            .Include(r => r.ReplayType)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return replays.Select(r => r.ToDto()).ToList();
    }

    public async Task<GameReplayDto?> GetGameReplayAsync(int userId, int gameId, int id)
    {
        var replay = await _context.GameReplays
            .Where(r => r.Id == id && r.GameId == gameId && r.UserId == userId)
            .Include(r => r.ReplayType)
            .FirstOrDefaultAsync();

        return replay?.ToDto();
    }

    public async Task<(GameReplayDto? Dto, string? Error)> CreateReplayAsync(int userId, int gameId, GameReplayCreateDto dto)
    {
        var gameExists = await _context.Games.AnyAsync(g => g.Id == gameId && g.UserId == userId);
        if (!gameExists) return (null, "Juego no encontrado");

        int replayTypeId;
        if (dto.ReplayTypeId.HasValue)
        {
            var typeExists = await _context.GameReplayTypes
                .AnyAsync(t => t.Id == dto.ReplayTypeId.Value && t.UserId == userId);
            if (!typeExists) return (null, "Tipo de rejugada no válido");
            replayTypeId = dto.ReplayTypeId.Value;
        }
        else
        {
            var specialType = await _context.GameReplayTypes
                .FirstOrDefaultAsync(t => t.UserId == userId && t.ReplayType == SpecialReplayType.Replay);
            if (specialType == null) return (null, "No hay tipo de rejugada especial configurado");
            replayTypeId = specialType.Id;
        }

        var replay = new GameReplay
        {
            GameId = gameId,
            ReplayTypeId = replayTypeId,
            Started = dto.Started,
            Finished = dto.Finished,
            Grade = dto.Grade,
            Notes = dto.Notes,
            Released = dto.Released,
            UserId = userId
        };

        _context.GameReplays.Add(replay);
        await _context.SaveChangesAsync();

        await _context.Entry(replay).Reference(r => r.ReplayType).LoadAsync();
        return (replay.ToDto(), null);
    }

    public async Task<(bool Success, string? Error)> UpdateReplayAsync(int userId, int gameId, int id, GameReplayUpdateDto dto)
    {
        var replay = await _context.GameReplays
            .FirstOrDefaultAsync(r => r.Id == id && r.GameId == gameId && r.UserId == userId);
        if (replay == null) return (false, null);

        if (dto.ReplayTypeId.HasValue)
        {
            var typeExists = await _context.GameReplayTypes
                .AnyAsync(t => t.Id == dto.ReplayTypeId.Value && t.UserId == userId);
            if (!typeExists) return (false, "Tipo de rejugada no válido");
            replay.ReplayTypeId = dto.ReplayTypeId.Value;
        }

        replay.Started = dto.Started;
        replay.Finished = dto.Finished;
        replay.Grade = dto.Grade;
        replay.Notes = dto.Notes;
        replay.Released = dto.Released;

        await _context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<bool> DeleteReplayAsync(int userId, int gameId, int id)
    {
        var replay = await _context.GameReplays
            .FirstOrDefaultAsync(r => r.Id == id && r.GameId == gameId && r.UserId == userId);
        if (replay == null) return false;

        _context.GameReplays.Remove(replay);
        await _context.SaveChangesAsync();
        return true;
    }
}

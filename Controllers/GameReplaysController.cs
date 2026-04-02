using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Helpers;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/games/{gameId}/replays")]
[Authorize]
public class GameReplaysController : BaseApiController
{
    private readonly GamesDbContext _context;

    public GameReplaysController(GamesDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<GameReplayDto>>> GetReplaysForGame(int gameId)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        var gameExists = await _context.Games.AnyAsync(g => g.Id == gameId && g.UserId == userId);
        if (!gameExists)
            return NotFound(new { message = "Juego no encontrado" });

        var replays = await _context.GameReplays
            .Where(r => r.GameId == gameId && r.UserId == userId)
            .Include(r => r.ReplayType)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(replays.Select(r => r.ToDto()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GameReplayDto>> GetGameReplay(int gameId, int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var replay = await _context.GameReplays
            .Where(r => r.Id == id && r.GameId == gameId && r.UserId == userId)
            .Include(r => r.ReplayType)
            .FirstOrDefaultAsync();

        return replay == null ? NotFound() : Ok(replay.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<GameReplayDto>> PostGameReplay(int gameId, GameReplayCreateDto createDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        var gameExists = await _context.Games.AnyAsync(g => g.Id == gameId && g.UserId == userId);
        if (!gameExists)
            return BadRequest(new { message = "Juego no encontrado" });

        int replayTypeId;
        if (createDto.ReplayTypeId.HasValue)
        {
            var typeExists = await _context.GameReplayTypes
                .AnyAsync(t => t.Id == createDto.ReplayTypeId.Value && t.UserId == userId);
            if (!typeExists)
                return BadRequest(new { message = "Tipo de rejugada no válido" });
            replayTypeId = createDto.ReplayTypeId.Value;
        }
        else
        {
            var specialType = await _context.GameReplayTypes
                .FirstOrDefaultAsync(t => t.UserId == userId && t.ReplayType == SpecialReplayType.Replay);
            if (specialType == null)
                return BadRequest(new { message = "No hay tipo de rejugada especial configurado" });
            replayTypeId = specialType.Id;
        }

        var replay = new GameReplay
        {
            GameId = gameId,
            ReplayTypeId = replayTypeId,
            Started = createDto.Started,
            Finished = createDto.Finished,
            Grade = createDto.Grade,
            Notes = createDto.Notes,
            UserId = userId
        };

        _context.GameReplays.Add(replay);
        await _context.SaveChangesAsync();

        await _context.Entry(replay).Reference(r => r.ReplayType).LoadAsync();

        return CreatedAtAction(nameof(GetGameReplay), new { gameId, id = replay.Id }, replay.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGameReplay(int gameId, int id, GameReplayUpdateDto updateDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var replay = await _context.GameReplays
            .FirstOrDefaultAsync(r => r.Id == id && r.GameId == gameId && r.UserId == userId);

        if (replay == null)
            return NotFound();

        if (updateDto.ReplayTypeId.HasValue)
        {
            var typeExists = await _context.GameReplayTypes
                .AnyAsync(t => t.Id == updateDto.ReplayTypeId.Value && t.UserId == userId);
            if (!typeExists)
                return BadRequest(new { message = "Tipo de rejugada no válido" });
            replay.ReplayTypeId = updateDto.ReplayTypeId.Value;
        }

        replay.Started = updateDto.Started;
        replay.Finished = updateDto.Finished;
        replay.Grade = updateDto.Grade;
        replay.Notes = updateDto.Notes;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGameReplay(int gameId, int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var replay = await _context.GameReplays
            .FirstOrDefaultAsync(r => r.Id == id && r.GameId == gameId && r.UserId == userId);

        if (replay == null)
            return NotFound();

        _context.GameReplays.Remove(replay);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}

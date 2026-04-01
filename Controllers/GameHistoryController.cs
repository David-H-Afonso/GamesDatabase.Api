using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Helpers;
using GamesDatabase.Api.Models;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GameHistoryController : BaseApiController
{
    private readonly GamesDbContext _context;

    public GameHistoryController(GamesDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Historial de un juego concreto.
    /// </summary>
    [HttpGet("games/{gameId}")]
    public async Task<ActionResult<PagedResult<GameHistoryEntryDto>>> GetGameHistory(
        int gameId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        var gameExists = await _context.Games.AnyAsync(g => g.Id == gameId && g.UserId == userId);
        if (!gameExists)
            return NotFound(new { message = "Juego no encontrado" });

        var query = _context.GameHistoryEntries
            .Where(e => e.GameId == gameId && e.UserId == userId)
            .OrderByDescending(e => e.ChangedAt);

        var total = await query.CountAsync();
        var skip = (page - 1) * pageSize;
        var entries = await query.Skip(skip).Take(pageSize).ToListAsync();

        return Ok(new PagedResult<GameHistoryEntryDto>
        {
            Data = entries.Select(e => e.ToDto()),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// Auditoría global: todos los cambios del usuario autenticado en todos sus juegos.
    /// Incluye entradas de juegos ya eliminados (GameId = null, GameName persiste).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<GameHistoryEntryDto>>> GetAllHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? actionType = null,
        [FromQuery] string? field = null,
        [FromQuery] int? gameId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        var query = _context.GameHistoryEntries
            .Where(e => e.UserId == userId)
            .AsQueryable();

        if (!string.IsNullOrEmpty(actionType))
            query = query.Where(e => e.ActionType == actionType);

        if (!string.IsNullOrEmpty(field))
            query = query.Where(e => e.Field == field);

        if (gameId.HasValue)
            query = query.Where(e => e.GameId == gameId.Value);

        if (from.HasValue)
            query = query.Where(e => e.ChangedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.ChangedAt <= to.Value);

        query = query.OrderByDescending(e => e.ChangedAt);

        var total = await query.CountAsync();
        var skip = (page - 1) * pageSize;
        var entries = await query.Skip(skip).Take(pageSize).ToListAsync();

        return Ok(new PagedResult<GameHistoryEntryDto>
        {
            Data = entries.Select(e => e.ToDto()),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpDelete("games/{gameId}/entries/{entryId}")]
    public async Task<IActionResult> DeleteHistoryEntry(int gameId, int entryId)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var entry = await _context.GameHistoryEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.GameId == gameId && e.UserId == userId);

        if (entry == null)
            return NotFound();

        _context.GameHistoryEntries.Remove(entry);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("games/{gameId}")]
    public async Task<IActionResult> DeleteAllGameHistory(int gameId)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        var entries = await _context.GameHistoryEntries
            .Where(e => e.GameId == gameId && e.UserId == userId)
            .ToListAsync();

        if (entries.Count == 0)
            return NotFound(new { message = "No hay entradas de historial para este juego" });

        _context.GameHistoryEntries.RemoveRange(entries);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Admin: historial global de todos los usuarios. Solo accesible con rol Admin.
    /// </summary>
    [HttpGet("admin")]
    public async Task<ActionResult<PagedResult<GameHistoryEntryDto>>> GetAdminHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] int? userId = null,
        [FromQuery] string? actionType = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var currentUserId = GetCurrentUserIdOrDefault(1);
        var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId);
        if (currentUser?.Role != UserRole.Admin)
            return Forbid();

        var query = _context.GameHistoryEntries.AsQueryable();

        if (userId.HasValue)
            query = query.Where(e => e.UserId == userId.Value);

        if (!string.IsNullOrEmpty(actionType))
            query = query.Where(e => e.ActionType == actionType);

        if (from.HasValue)
            query = query.Where(e => e.ChangedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.ChangedAt <= to.Value);

        query = query.OrderByDescending(e => e.ChangedAt);

        var total = await query.CountAsync();
        var skip = (page - 1) * pageSize;
        var entries = await query.Skip(skip).Take(pageSize).ToListAsync();

        return Ok(new PagedResult<GameHistoryEntryDto>
        {
            Data = entries.Select(e => e.ToDto()),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        });
    }
}

using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Services;

public class GameHistoryService : IGameHistoryService
{
    private const int MaxEntriesPerGame = 200;

    private readonly GamesDbContext _context;

    public GameHistoryService(GamesDbContext context)
    {
        _context = context;
    }

    public async Task RecordCreatedAsync(Game game, int userId)
    {
        var entries = BuildCreatedEntries(game, userId);
        await PersistEntriesAsync(entries);
    }

    public async Task RecordUpdatedAsync(
        Game before,
        System.Text.Json.JsonElement patch,
        int userId,
        string? oldStatusName, string? newStatusName,
        string? oldPlatformName, string? newPlatformName,
        string? oldPlayedStatusName, string? newPlayedStatusName)
    {
        var entries = BuildUpdatedEntries(
            before, patch, userId,
            oldStatusName, newStatusName,
            oldPlatformName, newPlatformName,
            oldPlayedStatusName, newPlayedStatusName);

        await PersistEntriesAsync(entries);
    }

    public async Task RecordDeletedAsync(Game game, int userId)
    {
        var entry = new GameHistoryEntry
        {
            GameId = game.Id,
            GameName = game.Name,
            UserId = userId,
            Field = "Game",
            OldValue = game.Name,
            NewValue = null,
            Description = $"Juego eliminado: {game.Name}",
            ActionType = "Deleted",
            ChangedAt = DateTime.UtcNow
        };

        _context.GameHistoryEntries.Add(entry);
        await _context.SaveChangesAsync();
    }

    // ─── private helpers ────────────────────────────────────────────────────────

    private static List<GameHistoryEntry> BuildCreatedEntries(Game game, int userId)
    {
        var now = DateTime.UtcNow;
        var entries = new List<GameHistoryEntry>();

        void Add(string field, string? value, string description)
        {
            if (value == null) return;
            entries.Add(new GameHistoryEntry
            {
                GameId = game.Id,
                GameName = game.Name,
                UserId = userId,
                Field = field,
                OldValue = null,
                NewValue = value,
                Description = description,
                ActionType = "Created",
                ChangedAt = now
            });
        }

        Add("Name", game.Name, $"Juego creado: {game.Name}");
        Add("Status", null, null!); // se añade siempre con el nombre ya resuelto por el llamador — ver nota abajo
        // El llamador ya tiene el statusName cargado vía Include, pasamos el nombre directamente
        entries.Add(new GameHistoryEntry
        {
            GameId = game.Id,
            GameName = game.Name,
            UserId = userId,
            Field = "Status",
            OldValue = null,
            NewValue = game.Status?.Name ?? game.StatusId.ToString(),
            Description = $"Estado inicial: {game.Status?.Name ?? game.StatusId.ToString()}",
            ActionType = "Created",
            ChangedAt = now
        });
        entries.RemoveAll(e => e.Field == "Status" && e.NewValue == null); // limpia el placeholder

        if (game.PlatformId.HasValue)
            Add("Platform", game.Platform?.Name ?? game.PlatformId.ToString(), $"Plataforma: {game.Platform?.Name ?? game.PlatformId.ToString()}");

        if (game.Grade.HasValue)
            Add("Grade", game.Grade.ToString(), $"Nota: {game.Grade}");

        if (game.Critic.HasValue)
            Add("Critic", game.Critic.ToString(), $"Crítica: {game.Critic}");

        if (!string.IsNullOrEmpty(game.CriticProvider))
            Add("CriticProvider", game.CriticProvider, $"Proveedor crítica: {game.CriticProvider}");

        if (game.Story.HasValue)
            Add("Story", game.Story.ToString(), $"Historia: {game.Story}h");

        if (game.Completion.HasValue)
            Add("Completion", game.Completion.ToString(), $"Completado: {game.Completion}h");

        if (!string.IsNullOrEmpty(game.Released))
            Add("Released", game.Released, $"Fecha de lanzamiento: {game.Released}");

        if (!string.IsNullOrEmpty(game.Started))
            Add("Started", game.Started, $"Inicio: {game.Started}");

        if (!string.IsNullOrEmpty(game.Finished))
            Add("Finished", game.Finished, $"Fin: {game.Finished}");

        if (!string.IsNullOrEmpty(game.Comment))
            Add("Comment", game.Comment, "Comentario añadido");

        if (game.PlayedStatusId.HasValue)
            Add("PlayedStatus", game.PlayedStatus?.Name ?? game.PlayedStatusId.ToString(), $"Estado de juego: {game.PlayedStatus?.Name ?? game.PlayedStatusId.ToString()}");

        if (!string.IsNullOrEmpty(game.Cover))
            Add("Cover", game.Cover, "Portada establecida");

        if (!string.IsNullOrEmpty(game.Logo))
            Add("Logo", game.Logo, "Logo establecido");

        if (game.IsCheaperByKey.HasValue)
            Add("IsCheaperByKey", game.IsCheaperByKey.Value ? "Sí" : "No", $"Más barato por clave: {(game.IsCheaperByKey.Value ? "Sí" : "No")}");

        if (!string.IsNullOrEmpty(game.KeyStoreUrl))
            Add("KeyStoreUrl", game.KeyStoreUrl, "URL de tienda de claves establecida");

        return entries;
    }

    private static List<GameHistoryEntry> BuildUpdatedEntries(
        Game before,
        System.Text.Json.JsonElement patch,
        int userId,
        string? oldStatusName, string? newStatusName,
        string? oldPlatformName, string? newPlatformName,
        string? oldPlayedStatusName, string? newPlayedStatusName)
    {
        var now = DateTime.UtcNow;
        var entries = new List<GameHistoryEntry>();

        GameHistoryEntry? Changed(string field, string? oldVal, string? newVal, string description)
        {
            if (oldVal == newVal) return null;
            return new GameHistoryEntry
            {
                GameId = before.Id,
                GameName = before.Name,
                UserId = userId,
                Field = field,
                OldValue = oldVal,
                NewValue = newVal,
                Description = description,
                ActionType = "Updated",
                ChangedAt = now
            };
        }

        if (patch.TryGetProperty("name", out var nameEl) && nameEl.ValueKind != System.Text.Json.JsonValueKind.Null)
        {
            var newName = nameEl.GetString();
            if (entries is var _ && before.Name != newName)
                entries.Add(Changed("Name", before.Name, newName, $"Nombre cambiado de \"{before.Name}\" a \"{newName}\"")!);
        }

        if (patch.TryGetProperty("statusId", out var statusEl) && statusEl.ValueKind != System.Text.Json.JsonValueKind.Null)
        {
            var newId = statusEl.GetInt32();
            if (before.StatusId != newId)
                entries.Add(Changed("Status", oldStatusName, newStatusName, $"Estado cambiado de \"{oldStatusName}\" a \"{newStatusName}\"")!);
        }

        if (patch.TryGetProperty("grade", out var gradeEl))
        {
            var newGrade = gradeEl.ValueKind == System.Text.Json.JsonValueKind.Null ? (int?)null : gradeEl.GetInt32();
            if (before.Grade != newGrade)
                entries.Add(Changed("Grade", before.Grade?.ToString(), newGrade?.ToString(), $"Nota cambiada de {before.Grade?.ToString() ?? "—"} a {newGrade?.ToString() ?? "—"}")!);
        }

        if (patch.TryGetProperty("critic", out var criticEl))
        {
            var newCritic = criticEl.ValueKind == System.Text.Json.JsonValueKind.Null ? (int?)null : criticEl.GetInt32();
            if (before.Critic != newCritic)
                entries.Add(Changed("Critic", before.Critic?.ToString(), newCritic?.ToString(), $"Crítica cambiada de {before.Critic?.ToString() ?? "—"} a {newCritic?.ToString() ?? "—"}")!);
        }

        if (patch.TryGetProperty("criticProvider", out var criticProviderEl))
        {
            var newVal = criticProviderEl.ValueKind == System.Text.Json.JsonValueKind.Null ? null : criticProviderEl.GetString();
            if (before.CriticProvider != newVal)
                entries.Add(Changed("CriticProvider", before.CriticProvider, newVal, $"Proveedor crítica: '{before.CriticProvider ?? "—"}' → '{newVal ?? "—"}'")!);
        }

        if (patch.TryGetProperty("story", out var storyEl))
        {
            var newVal = storyEl.ValueKind == System.Text.Json.JsonValueKind.Null ? (int?)null : storyEl.GetInt32();
            if (before.Story != newVal)
                entries.Add(Changed("Story", before.Story?.ToString(), newVal?.ToString(), $"Historia: {before.Story?.ToString() ?? "—"}h → {newVal?.ToString() ?? "—"}h")!);
        }

        if (patch.TryGetProperty("completion", out var completionEl))
        {
            var newVal = completionEl.ValueKind == System.Text.Json.JsonValueKind.Null ? (int?)null : completionEl.GetInt32();
            if (before.Completion != newVal)
                entries.Add(Changed("Completion", before.Completion?.ToString(), newVal?.ToString(), $"Completado: {before.Completion?.ToString() ?? "—"}h → {newVal?.ToString() ?? "—"}h")!);
        }

        if (patch.TryGetProperty("platformId", out var platformEl))
        {
            var newId = platformEl.ValueKind == System.Text.Json.JsonValueKind.Null ? (int?)null : platformEl.GetInt32();
            if (before.PlatformId != newId)
                entries.Add(Changed("Platform", oldPlatformName, newPlatformName, $"Plataforma: '{oldPlatformName ?? "—"}' → '{newPlatformName ?? "—"}'")!);
        }

        if (patch.TryGetProperty("released", out var releasedEl))
        {
            var newVal = releasedEl.ValueKind == System.Text.Json.JsonValueKind.Null ? null : releasedEl.GetString();
            if (before.Released != newVal)
                entries.Add(Changed("Released", before.Released, newVal, $"Lanzamiento: '{before.Released ?? "—"}' → '{newVal ?? "—"}'")!);
        }

        if (patch.TryGetProperty("started", out var startedEl))
        {
            var newVal = startedEl.ValueKind == System.Text.Json.JsonValueKind.Null ? null : startedEl.GetString();
            if (before.Started != newVal)
                entries.Add(Changed("Started", before.Started, newVal, $"Inicio: '{before.Started ?? "—"}' → '{newVal ?? "—"}'")!);
        }

        if (patch.TryGetProperty("finished", out var finishedEl))
        {
            var newVal = finishedEl.ValueKind == System.Text.Json.JsonValueKind.Null ? null : finishedEl.GetString();
            if (before.Finished != newVal)
                entries.Add(Changed("Finished", before.Finished, newVal, $"Fin: '{before.Finished ?? "—"}' → '{newVal ?? "—"}'")!);
        }

        if (patch.TryGetProperty("comment", out var commentEl))
        {
            var newVal = commentEl.ValueKind == System.Text.Json.JsonValueKind.Null ? null : commentEl.GetString();
            if (before.Comment != newVal)
                entries.Add(Changed("Comment", before.Comment, newVal, "Comentario actualizado")!);
        }

        if (patch.TryGetProperty("playedStatusId", out var playedStatusEl))
        {
            var newId = playedStatusEl.ValueKind == System.Text.Json.JsonValueKind.Null ? (int?)null : playedStatusEl.GetInt32();
            if (before.PlayedStatusId != newId)
                entries.Add(Changed("PlayedStatus", oldPlayedStatusName, newPlayedStatusName, $"Estado de juego: '{oldPlayedStatusName ?? "—"}' → '{newPlayedStatusName ?? "—"}'")!);
        }

        if (patch.TryGetProperty("logo", out var logoEl))
        {
            var newVal = logoEl.ValueKind == System.Text.Json.JsonValueKind.Null ? null : logoEl.GetString();
            if (before.Logo != newVal)
                entries.Add(Changed("Logo", before.Logo, newVal, newVal == null ? "Logo eliminado" : "Logo actualizado")!);
        }

        if (patch.TryGetProperty("cover", out var coverEl))
        {
            var newVal = coverEl.ValueKind == System.Text.Json.JsonValueKind.Null ? null : coverEl.GetString();
            if (before.Cover != newVal)
                entries.Add(Changed("Cover", before.Cover, newVal, newVal == null ? "Portada eliminada" : "Portada actualizada")!);
        }

        if (patch.TryGetProperty("isCheaperByKey", out var isCheaperEl))
        {
            var newVal = isCheaperEl.ValueKind == System.Text.Json.JsonValueKind.Null ? (bool?)null : isCheaperEl.GetBoolean();
            if (before.IsCheaperByKey != newVal)
                entries.Add(Changed("IsCheaperByKey", before.IsCheaperByKey?.ToString(), newVal?.ToString(), $"Más barato por clave: {before.IsCheaperByKey?.ToString() ?? "—"} → {newVal?.ToString() ?? "—"}")!);
        }

        if (patch.TryGetProperty("keyStoreUrl", out var keyStoreEl))
        {
            var newVal = keyStoreEl.ValueKind == System.Text.Json.JsonValueKind.Null ? null : keyStoreEl.GetString();
            if (before.KeyStoreUrl != newVal)
                entries.Add(Changed("KeyStoreUrl", before.KeyStoreUrl, newVal, newVal == null ? "URL tienda de claves eliminada" : "URL tienda de claves actualizada")!);
        }

        return entries.Where(e => e != null).ToList();
    }

    private async Task PersistEntriesAsync(List<GameHistoryEntry> entries)
    {
        if (entries.Count == 0) return;

        // Agrupamos por GameId para aplicar el límite de 200 por juego
        foreach (var group in entries.GroupBy(e => e.GameId))
        {
            if (!group.Key.HasValue) continue;

            var gameId = group.Key.Value;
            var userId = group.First().UserId;
            var incoming = group.ToList();

            var currentCount = await _context.GameHistoryEntries
                .CountAsync(e => e.GameId == gameId && e.UserId == userId);

            var totalAfter = currentCount + incoming.Count;
            if (totalAfter > MaxEntriesPerGame)
            {
                var toDelete = totalAfter - MaxEntriesPerGame;
                var oldest = await _context.GameHistoryEntries
                    .Where(e => e.GameId == gameId && e.UserId == userId)
                    .OrderBy(e => e.ChangedAt)
                    .Take(toDelete)
                    .ToListAsync();
                _context.GameHistoryEntries.RemoveRange(oldest);
            }
        }

        _context.GameHistoryEntries.AddRange(entries);
        await _context.SaveChangesAsync();
    }
}

using System.Globalization;
using GamesDatabase.Api.Controllers;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Application.Services;

/// <summary>Shared view metadata for full CSV and one-shot/scheduled backup exports.</summary>
internal static class GameViewArchive
{
    public static FullExportModel Export(GameView view) => new()
    {
        Type = "View", Name = view.Name, Description = view.Description ?? "",
        FiltersJson = view.FiltersJson, SortingJson = view.SortingJson ?? "",
        IsPublic = view.IsPublic.ToString(), CreatedBy = view.CreatedBy ?? "",
        SortOrder = view.SortOrder.ToString(CultureInfo.InvariantCulture),
        ViewPinnedSlot = view.PinnedSlot?.ToString(CultureInfo.InvariantCulture) ?? "",
        ViewLastUsedAt = view.LastUsedAt.HasValue ? DateTime.SpecifyKind(view.LastUsedAt.Value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture) : ""
    };

    // Merge, never evict a destination pin or regress its recent-use timestamp.
    // Blank/absent legacy columns leave destination preferences unchanged.
    public static void MergePreferences(GameView view, FullExportModel record, HashSet<int> occupiedSlots, List<string> errors)
    {
        if (!string.IsNullOrWhiteSpace(record.SortOrder))
        {
            if (int.TryParse(record.SortOrder, NumberStyles.Integer, CultureInfo.InvariantCulture, out var order) && order >= 0) view.SortOrder = order;
            else errors.Add($"View '{view.Name}': invalid sort order; existing order retained.");
        }
        if (!string.IsNullOrWhiteSpace(record.ViewLastUsedAt))
        {
            if (DateTimeOffset.TryParse(record.ViewLastUsedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            {
                if (!view.LastUsedAt.HasValue || timestamp.UtcDateTime > view.LastUsedAt.Value) view.LastUsedAt = timestamp.UtcDateTime;
            }
            else errors.Add($"View '{view.Name}': invalid last use; existing timestamp retained.");
        }
        if (!view.IsPublic)
        {
            if (view.PinnedSlot.HasValue) occupiedSlots.Remove(view.PinnedSlot.Value);
            view.PinnedSlot = null;
            return;
        }
        if (string.IsNullOrWhiteSpace(record.ViewPinnedSlot)) return;
        if (!int.TryParse(record.ViewPinnedSlot, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot) || slot is not (1 or 2))
        {
            errors.Add($"View '{view.Name}': invalid pin slot; destination pins retained.");
            return;
        }
        if (view.PinnedSlot == slot) return;
        if (view.PinnedSlot.HasValue || occupiedSlots.Contains(slot))
        {
            errors.Add($"View '{view.Name}': pin slot unavailable; destination pins retained.");
            return;
        }
        view.PinnedSlot = slot;
        occupiedSlots.Add(slot);
    }
}

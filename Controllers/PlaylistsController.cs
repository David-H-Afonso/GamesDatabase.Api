using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PlaylistsController(IPlaylistService playlistService) : BaseApiController
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PlaylistSummaryDto>>> GetPlaylists() => Ok(await playlistService.GetPlaylistsAsync(GetCurrentUserIdOrDefault(1)));

    [HttpGet("{id}")]
    public async Task<ActionResult<PlaylistDto>> GetPlaylist(int id)
    {
        var playlist = await playlistService.GetPlaylistByIdAsync(id, GetCurrentUserIdOrDefault(1));
        return playlist is null ? NotFound("Playlist no encontrada.") : Ok(playlist);
    }

    [HttpPost]
    public async Task<ActionResult<PlaylistDto>> CreatePlaylist(PlaylistCreateDto dto)
    {
        var result = await playlistService.CreatePlaylistAsync(dto, GetCurrentUserIdOrDefault(1));
        return ToActionResult(result, nameof(GetPlaylist));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<PlaylistDto>> UpdatePlaylist(int id, PlaylistUpdateDto dto)
    {
        var result = await playlistService.UpdatePlaylistAsync(id, dto, GetCurrentUserIdOrDefault(1));
        return ToActionResult(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePlaylist(int id)
    {
        var result = await playlistService.DeletePlaylistAsync(id, GetCurrentUserIdOrDefault(1));
        return result.Success ? NoContent() : ErrorResult(result);
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderPlaylists(ReorderPlaylistsDto dto)
    {
        var result = await playlistService.ReorderPlaylistsAsync(dto, GetCurrentUserIdOrDefault(1));
        return result.Success ? Ok() : ErrorResult(result);
    }

    [HttpPost("{id}/items")]
    public async Task<ActionResult<PlaylistDto>> AddItem(int id, AddPlaylistItemDto dto)
    {
        var result = await playlistService.AddItemAsync(id, dto, GetCurrentUserIdOrDefault(1));
        return ToActionResult(result);
    }

    [HttpDelete("{id}/items/{itemId}")]
    public async Task<ActionResult<PlaylistDto>> RemoveItem(int id, int itemId)
    {
        var result = await playlistService.RemoveItemAsync(id, itemId, GetCurrentUserIdOrDefault(1));
        return ToActionResult(result);
    }

    [HttpPost("{id}/items/reorder")]
    public async Task<IActionResult> ReorderItems(int id, ReorderPlaylistItemsDto dto)
    {
        var result = await playlistService.ReorderItemsAsync(id, dto, GetCurrentUserIdOrDefault(1));
        return result.Success ? Ok() : ErrorResult(result);
    }

    private ActionResult<PlaylistDto> ToActionResult(CatalogServiceResult<PlaylistDto> result, string? actionName = null)
    {
        if (result.Success) return actionName is null ? Ok(result.Data) : CreatedAtAction(actionName, new { id = result.Data!.Id }, result.Data);
        return result.NotFound ? NotFound(result.Error) : result.Conflict ? Conflict(result.Error) : result.StatusCode == 500 ? StatusCode(500, result.Error) : BadRequest(result.Error);
    }

    private IActionResult ErrorResult(CatalogServiceResult result) => result.NotFound ? NotFound(result.Error) : result.StatusCode == 500 ? StatusCode(500, result.Error) : BadRequest(result.Error);
}

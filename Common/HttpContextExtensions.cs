namespace GamesDatabase.Api.Common;

public static class HttpContextHelper
{
    public static int? GetUserId(this HttpContext context)
    {
        if (context.Items.TryGetValue("UserId", out var userId) && userId is int id)
        {
            return id;
        }

        var claim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(claim, out var claimUserId))
        {
            return claimUserId;
        }

        return null;
    }

    public static int GetUserIdOrDefault(this HttpContext context, int defaultUserId = 1)
    {
        return context.GetUserId() ?? defaultUserId;
    }
}

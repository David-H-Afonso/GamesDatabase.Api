using System.Linq.Expressions;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Common;

/// <summary>Raw Steam time and manual adjustment stay separate. Zero means automatic.</summary>
public static class GamePlaytime
{
    public const int MaximumManualMinutes = 600_000;
    public static int? NormalizeManual(int? minutes) => minutes is > 0 ? minutes : null;
    public static long? Effective(int? manual, int? steam, bool additive) => manual is > 0
        ? (long)manual.Value + (additive ? (long)(steam ?? 0) : 0L)
        : steam;

    // Keep SQL filtering/sorting equivalent to the DTO/UI policy; a method call
    // inside an IQueryable would not translate reliably across EF providers.
    public static readonly Expression<Func<Game, long?>> EffectiveExpression = game => game.ManualPlaytimeMinutes > 0
        ? (long?)game.ManualPlaytimeMinutes + (game.ManualPlaytimeIsAdditive ? (long)(game.SteamPlaytimeForever ?? 0) : 0L)
        : (long?)game.SteamPlaytimeForever;
    public static Expression For(ParameterExpression parameter) => new Substitute(EffectiveExpression.Parameters[0], parameter).Visit(EffectiveExpression.Body)!;
    private sealed class Substitute(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? target : base.VisitParameter(node);
    }
}

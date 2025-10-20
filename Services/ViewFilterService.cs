using GamesDatabase.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Reflection;

namespace GamesDatabase.Api.Services;

public interface IViewFilterService
{
    IQueryable<Game> ApplyFilters(IQueryable<Game> query, ViewConfiguration configuration, int userId);
}

public class ViewFilterService : IViewFilterService
{
    public IQueryable<Game> ApplyFilters(IQueryable<Game> query, ViewConfiguration configuration, int userId)
    {
        query = query.Where(g => g.UserId == userId);

        // Aplicar grupos de filtros
        if (configuration.FilterGroups.Any())
        {
            var parameter = Expression.Parameter(typeof(Game), "g");
            Expression? groupsCombined = null;

            foreach (var group in configuration.FilterGroups)
            {
                if (!group.Filters.Any()) continue;

                Expression? groupExpression = null;

                // Combinar filtros dentro del grupo
                foreach (var filter in group.Filters)
                {
                    Expression propertyExpression = GetPropertyExpression(parameter, filter.Field);
                    Expression filterExpression = CreateFilterExpression(propertyExpression, filter.Operator, filter.Value, filter.SecondValue);

                    if (groupExpression == null)
                    {
                        groupExpression = filterExpression;
                    }
                    else
                    {
                        groupExpression = group.CombineWith == FilterLogic.Or
                            ? Expression.OrElse(groupExpression, filterExpression)
                            : Expression.AndAlso(groupExpression, filterExpression);
                    }
                }

                if (groupExpression != null)
                {
                    if (groupsCombined == null)
                    {
                        groupsCombined = groupExpression;
                    }
                    else
                    {
                        groupsCombined = configuration.GroupCombineWith == FilterLogic.Or
                            ? Expression.OrElse(groupsCombined, groupExpression)
                            : Expression.AndAlso(groupsCombined, groupExpression);
                    }
                }
            }

            if (groupsCombined != null)
            {
                var lambda = Expression.Lambda<Func<Game, bool>>(groupsCombined, parameter);
                query = query.Where(lambda);
            }
        }

        // Aplicar ordenamientos
        if (configuration.Sorting.Any())
        {
            query = ApplySorting(query, configuration.Sorting);
        }

        return query;
    }

    private IQueryable<Game> ApplyFilter(IQueryable<Game> query, ViewFilter filter)
    {
        var parameter = Expression.Parameter(typeof(Game), "g");
        Expression propertyExpression = GetPropertyExpression(parameter, filter.Field);
        Expression filterExpression = CreateFilterExpression(propertyExpression, filter.Operator, filter.Value, filter.SecondValue);

        var lambda = Expression.Lambda<Func<Game, bool>>(filterExpression, parameter);
        return query.Where(lambda);
    }

    private Expression GetPropertyExpression(ParameterExpression parameter, FilterField field)
    {
        return field switch
        {
            FilterField.Name => Expression.Property(parameter, nameof(Game.Name)),
            FilterField.StatusId => Expression.Property(parameter, nameof(Game.StatusId)),
            FilterField.PlatformId => Expression.Property(parameter, nameof(Game.PlatformId)),
            FilterField.PlayWithId => throw new NotSupportedException("PlayWithId filtering is not supported with many-to-many relationship. Use custom filters instead."),
            FilterField.PlayedStatusId => Expression.Property(parameter, nameof(Game.PlayedStatusId)),
            FilterField.Grade => Expression.Property(parameter, nameof(Game.Grade)),
            FilterField.Critic => Expression.Property(parameter, nameof(Game.Critic)),
            FilterField.Story => Expression.Property(parameter, nameof(Game.Story)),
            FilterField.Completion => Expression.Property(parameter, nameof(Game.Completion)),
            FilterField.Score => Expression.Property(parameter, nameof(Game.Score)),
            FilterField.Released => Expression.Property(parameter, nameof(Game.Released)),
            FilterField.Started => Expression.Property(parameter, nameof(Game.Started)),
            FilterField.Finished => Expression.Property(parameter, nameof(Game.Finished)),
            FilterField.Comment => Expression.Property(parameter, nameof(Game.Comment)),
            FilterField.CreatedAt => Expression.Property(parameter, nameof(Game.CreatedAt)),
            FilterField.UpdatedAt => Expression.Property(parameter, nameof(Game.UpdatedAt)),
            _ => throw new ArgumentException($"Campo de filtro no soportado: {field}")
        };
    }

    private Expression CreateFilterExpression(Expression propertyExpression, FilterOperator filterOperator, object? value, object? secondValue)
    {
        return filterOperator switch
        {
            FilterOperator.Equals => CreateEqualsExpression(propertyExpression, value),
            FilterOperator.NotEquals => Expression.Not(CreateEqualsExpression(propertyExpression, value)),
            FilterOperator.Contains => CreateContainsExpression(propertyExpression, value),
            FilterOperator.NotContains => Expression.Not(CreateContainsExpression(propertyExpression, value)),
            FilterOperator.GreaterThan => CreateComparisonExpression(propertyExpression, value, Expression.GreaterThan),
            FilterOperator.GreaterThanOrEqual => CreateComparisonExpression(propertyExpression, value, Expression.GreaterThanOrEqual),
            FilterOperator.LessThan => CreateComparisonExpression(propertyExpression, value, Expression.LessThan),
            FilterOperator.LessThanOrEqual => CreateComparisonExpression(propertyExpression, value, Expression.LessThanOrEqual),
            FilterOperator.Between => CreateBetweenExpression(propertyExpression, value, secondValue),
            FilterOperator.In => CreateInExpression(propertyExpression, value),
            FilterOperator.NotIn => Expression.Not(CreateInExpression(propertyExpression, value)),
            FilterOperator.IsNull => CreateIsNullExpression(propertyExpression),
            FilterOperator.IsNotNull => Expression.Not(CreateIsNullExpression(propertyExpression)),
            FilterOperator.StartsWith => CreateStartsWithExpression(propertyExpression, value),
            FilterOperator.EndsWith => CreateEndsWithExpression(propertyExpression, value),
            // Operadores específicos para fechas
            FilterOperator.On => CreateEqualsExpression(propertyExpression, value), // Fecha exacta
            FilterOperator.Before => CreateComparisonExpression(propertyExpression, value, Expression.LessThanOrEqual), // Antes o igual
            FilterOperator.After => CreateComparisonExpression(propertyExpression, value, Expression.GreaterThanOrEqual), // Después o igual
            _ => throw new ArgumentException($"Operador de filtro no soportado: {filterOperator}")
        };
    }

    private Expression CreateEqualsExpression(Expression propertyExpression, object? value)
    {
        if (value == null)
        {
            return Expression.Equal(propertyExpression, Expression.Constant(null));
        }

        // Manejar JsonElement
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            value = jsonElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => jsonElement.GetString(),
                System.Text.Json.JsonValueKind.Number => jsonElement.GetInt32(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                _ => jsonElement.ToString()
            };
        }

        // Manejo especial para fechas en campos string
        if (propertyExpression.Type == typeof(string) && value is string sVal)
        {
            // Intentar parsear como fecha para normalizar el formato
            if (DateTime.TryParse(sVal, out var parsed))
            {
                // Comparar usando formato ISO para consistencia
                var iso = parsed.ToString("yyyy-MM-dd");
                var constIso = Expression.Constant(iso, typeof(string));

                // Manejar el caso donde la propiedad puede ser null
                var nullCheck = Expression.NotEqual(propertyExpression, Expression.Constant(null));
                var equalsCheck = Expression.Equal(propertyExpression, constIso);
                return Expression.AndAlso(nullCheck, equalsCheck);
            }
        }

        var convertedValue = ConvertValue(value, propertyExpression.Type);
        var constantExpression = Expression.Constant(convertedValue, propertyExpression.Type);
        return Expression.Equal(propertyExpression, constantExpression);
    }

    private Expression CreateContainsExpression(Expression propertyExpression, object? value)
    {
        if (value == null)
        {
            return Expression.Constant(false);
        }

        // Solo permitir Contains en campos de tipo string
        if (propertyExpression.Type != typeof(string))
        {
            throw new ArgumentException(
                $"El operador 'Contains' solo puede usarse con campos de texto. " +
                $"El campo es de tipo '{propertyExpression.Type.Name}'. " +
                $"Para campos numéricos o de fecha, usa operadores como 'Equals', 'GreaterThan', 'LessThan', 'On', 'Before', 'After', etc.");
        }

        // Hacer la búsqueda case-insensitive convirtiendo a minúsculas
        var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes)!;
        var containsMethod = typeof(string).GetMethod("Contains", new[] { typeof(string) })!;

        // Convertir el valor de búsqueda a minúsculas
        var searchValueLower = value.ToString()!.ToLower();
        var valueExpression = Expression.Constant(searchValueLower, typeof(string));

        // Manejar el caso donde la propiedad puede ser null
        var nullCheck = Expression.NotEqual(propertyExpression, Expression.Constant(null));

        // Convertir la propiedad a minúsculas antes de hacer Contains
        var propertyToLower = Expression.Call(propertyExpression, toLowerMethod);
        var containsCall = Expression.Call(propertyToLower, containsMethod, valueExpression);

        return Expression.AndAlso(nullCheck, containsCall);
    }

    private Expression CreateComparisonExpression(Expression propertyExpression, object? value, Func<Expression, Expression, Expression> comparisonFunc)
    {
        if (value == null)
        {
            return Expression.Constant(false);
        }

        // Manejar JsonElement
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            value = jsonElement.ValueKind == System.Text.Json.JsonValueKind.String
                ? jsonElement.GetString()
                : jsonElement.ToString();
        }

        var targetType = propertyExpression.Type;

        // Special handling for string fields (like date strings)
        if (targetType == typeof(string) && value is string sVal)
        {
            // Try parse value as DateTime to normalize format
            if (DateTime.TryParse(sVal, out var parsed))
            {
                // Normalize to ISO format for lexicographic comparison
                var iso = parsed.ToString("yyyy-MM-dd");
                var constIso = Expression.Constant(iso, typeof(string));

                // Use string.CompareTo for comparison instead of direct operators
                // This works with EF Core translation
                var compareToMethod = typeof(string).GetMethod("CompareTo", new[] { typeof(string) })!;
                var nullCheck = Expression.NotEqual(propertyExpression, Expression.Constant(null, typeof(string)));
                var compareToCall = Expression.Call(propertyExpression, compareToMethod, constIso);
                var zero = Expression.Constant(0);

                // Map the comparison function to CompareTo result
                Expression comparison;
                if (comparisonFunc == Expression.GreaterThan)
                {
                    comparison = Expression.GreaterThan(compareToCall, zero);
                }
                else if (comparisonFunc == Expression.GreaterThanOrEqual)
                {
                    comparison = Expression.GreaterThanOrEqual(compareToCall, zero);
                }
                else if (comparisonFunc == Expression.LessThan)
                {
                    comparison = Expression.LessThan(compareToCall, zero);
                }
                else if (comparisonFunc == Expression.LessThanOrEqual)
                {
                    comparison = Expression.LessThanOrEqual(compareToCall, zero);
                }
                else
                {
                    // Fallback to direct comparison (may fail)
                    comparison = comparisonFunc(propertyExpression, constIso);
                }

                return Expression.AndAlso(nullCheck, comparison);
            }
        }

        // For non-string types or non-date strings
        var convertedValue = ConvertValue(value, propertyExpression.Type);
        var constantExpression = Expression.Constant(convertedValue, propertyExpression.Type);
        return comparisonFunc(propertyExpression, constantExpression);
    }

    private Expression CreateBetweenExpression(Expression propertyExpression, object? value, object? secondValue)
    {
        if (value == null || secondValue == null)
        {
            return Expression.Constant(false);
        }
        // If property is string and values look like dates, compare lexicographically on ISO date string
        if (propertyExpression.Type == typeof(string))
        {
            if (value is string s1 && secondValue is string s2 && DateTime.TryParse(s1, out var d1) && DateTime.TryParse(s2, out var d2))
            {
                var iso1 = d1.ToString("yyyy-MM-dd");
                var iso2 = d2.ToString("yyyy-MM-dd");
                var c1 = Expression.Constant(iso1, typeof(string));
                var c2 = Expression.Constant(iso2, typeof(string));

                var gte = Expression.GreaterThanOrEqual(propertyExpression, c1);
                var lte = Expression.LessThanOrEqual(propertyExpression, c2);
                return Expression.AndAlso(gte, lte);
            }
        }

        var convertedValue1 = ConvertValue(value, propertyExpression.Type);
        var convertedValue2 = ConvertValue(secondValue, propertyExpression.Type);

        var constant1 = Expression.Constant(convertedValue1, propertyExpression.Type);
        var constant2 = Expression.Constant(convertedValue2, propertyExpression.Type);

        var greaterThanOrEqual = Expression.GreaterThanOrEqual(propertyExpression, constant1);
        var lessThanOrEqual = Expression.LessThanOrEqual(propertyExpression, constant2);

        return Expression.AndAlso(greaterThanOrEqual, lessThanOrEqual);
    }

    private Expression CreateInExpression(Expression propertyExpression, object? value)
    {
        if (value == null)
        {
            return Expression.Constant(false);
        }

        // Convertir el valor a una lista si no lo es
        var valuesList = value is System.Collections.IEnumerable enumerable && !(value is string)
            ? enumerable.Cast<object>().ToList()
            : new List<object> { value };

        if (!valuesList.Any())
        {
            return Expression.Constant(false);
        }

        // Crear expresiones de igualdad para cada valor
        var equalityExpressions = valuesList.Select(v =>
        {
            var convertedValue = ConvertValue(v, propertyExpression.Type);
            var constantExpression = Expression.Constant(convertedValue, propertyExpression.Type);
            return Expression.Equal(propertyExpression, constantExpression);
        });

        // Combinar con OR
        return equalityExpressions.Aggregate(Expression.OrElse);
    }

    private Expression CreateIsNullExpression(Expression propertyExpression)
    {
        return Expression.Equal(propertyExpression, Expression.Constant(null));
    }

    private Expression CreateStartsWithExpression(Expression propertyExpression, object? value)
    {
        if (value == null)
        {
            return Expression.Constant(false);
        }

        if (propertyExpression.Type == typeof(string))
        {
            // Hacer la búsqueda case-insensitive convirtiendo a minúsculas
            var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes)!;
            var startsWithMethod = typeof(string).GetMethod("StartsWith", new[] { typeof(string) })!;

            // Convertir el valor de búsqueda a minúsculas
            var searchValueLower = value.ToString()!.ToLower();
            var valueExpression = Expression.Constant(searchValueLower, typeof(string));

            var nullCheck = Expression.NotEqual(propertyExpression, Expression.Constant(null));

            // Convertir la propiedad a minúsculas antes de hacer StartsWith
            var propertyToLower = Expression.Call(propertyExpression, toLowerMethod);
            var startsWithCall = Expression.Call(propertyToLower, startsWithMethod, valueExpression);

            return Expression.AndAlso(nullCheck, startsWithCall);
        }

        throw new ArgumentException($"StartsWith no es soportado para el tipo {propertyExpression.Type}");
    }

    private Expression CreateEndsWithExpression(Expression propertyExpression, object? value)
    {
        if (value == null)
        {
            return Expression.Constant(false);
        }

        if (propertyExpression.Type == typeof(string))
        {
            // Hacer la búsqueda case-insensitive convirtiendo a minúsculas
            var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes)!;
            var endsWithMethod = typeof(string).GetMethod("EndsWith", new[] { typeof(string) })!;

            // Convertir el valor de búsqueda a minúsculas
            var searchValueLower = value.ToString()!.ToLower();
            var valueExpression = Expression.Constant(searchValueLower, typeof(string));

            var nullCheck = Expression.NotEqual(propertyExpression, Expression.Constant(null));

            // Convertir la propiedad a minúsculas antes de hacer EndsWith
            var propertyToLower = Expression.Call(propertyExpression, toLowerMethod);
            var endsWithCall = Expression.Call(propertyToLower, endsWithMethod, valueExpression);

            return Expression.AndAlso(nullCheck, endsWithCall);
        }

        throw new ArgumentException($"EndsWith no es soportado para el tipo {propertyExpression.Type}");
    }

    private IQueryable<Game> ApplySorting(IQueryable<Game> query, List<ViewSort> sortings)
    {
        // Ordenar los sortings por Order
        var orderedSortings = sortings.OrderBy(s => s.Order).ToList();

        IOrderedQueryable<Game>? orderedQuery = null;

        for (int i = 0; i < orderedSortings.Count; i++)
        {
            var sort = orderedSortings[i];

            if (i == 0)
            {
                // Primer ordenamiento
                orderedQuery = sort.Direction == SortDirection.Ascending
                    ? query.OrderBy(GetSortExpression(sort.Field))
                    : query.OrderByDescending(GetSortExpression(sort.Field));
            }
            else
            {
                // Ordenamientos adicionales
                orderedQuery = sort.Direction == SortDirection.Ascending
                    ? orderedQuery!.ThenBy(GetSortExpression(sort.Field))
                    : orderedQuery!.ThenByDescending(GetSortExpression(sort.Field));
            }
        }

        return orderedQuery ?? query;
    }

    private Expression<Func<Game, object>> GetSortExpression(SortField field)
    {
        return field switch
        {
            SortField.Name => g => EF.Functions.Collate(g.Name, "NOCASE"),
            SortField.StatusId => g => g.StatusId,
            SortField.Status => g => g.Status.SortOrder,
            SortField.PlatformId => g => g.PlatformId ?? 0,
            SortField.Platform => g => g.Platform != null ? g.Platform.SortOrder : 0,
            SortField.PlayWithId => throw new NotSupportedException("PlayWithId sorting is not supported with many-to-many relationship."),
            SortField.PlayWith => throw new NotSupportedException("PlayWith sorting is not supported with many-to-many relationship."),
            SortField.PlayedStatusId => g => g.PlayedStatusId ?? 0,
            SortField.PlayedStatus => g => g.PlayedStatus != null ? g.PlayedStatus.SortOrder : 0,
            SortField.Grade => g => g.Grade ?? 0,
            SortField.Critic => g => g.Critic ?? 0,
            SortField.Story => g => g.Story ?? 0,
            SortField.Completion => g => g.Completion ?? 0,
            SortField.Score => g => g.Score.HasValue ? (double)g.Score.Value : 0.0,
            SortField.Released => g => g.Released ?? "",
            SortField.Started => g => g.Started ?? "",
            SortField.Finished => g => g.Finished ?? "",
            SortField.CreatedAt => g => g.CreatedAt,
            SortField.UpdatedAt => g => g.UpdatedAt,
            SortField.Id => g => g.Id,
            _ => throw new ArgumentException($"Campo de ordenamiento no soportado: {field}")
        };
    }

    private object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
            return null;

        // Manejar JsonElement si viene desde JSON
        if (value is System.Text.Json.JsonElement jsonElement)
        {
            value = jsonElement.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => jsonElement.GetString(),
                System.Text.Json.JsonValueKind.Number => jsonElement.GetInt32(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null => null,
                _ => jsonElement.ToString()
            };
        }

        if (value == null)
            return null;

        // Manejar tipos nullable
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            // Manejo especial para DateTime
            if (underlyingType == typeof(DateTime))
            {
                if (value is string stringValue)
                {
                    if (DateTime.TryParse(stringValue, out var dateValue))
                        return dateValue;
                }
                else if (value is DateTime)
                {
                    return value;
                }
            }

            // Manejo especial para decimales
            if (underlyingType == typeof(decimal))
            {
                if (value is string stringValue)
                {
                    if (decimal.TryParse(stringValue, out var decimalValue))
                        return decimalValue;
                }
                else if (value is IConvertible)
                {
                    return Convert.ToDecimal(value);
                }
            }

            // Manejo especial para enteros
            if (underlyingType == typeof(int))
            {
                if (value is string stringValue)
                {
                    if (int.TryParse(stringValue, out var intValue))
                        return intValue;
                }
                else if (value is IConvertible)
                {
                    return Convert.ToInt32(value);
                }
            }

            // Para otros tipos, intentar conversión directa
            return Convert.ChangeType(value, underlyingType);
        }
        catch
        {
            // Si la conversión falla, devolver null o el valor original
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }
    }
}
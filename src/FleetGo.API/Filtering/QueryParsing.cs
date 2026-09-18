using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace FleetGo.API.Filtering;

/// <summary>
/// Manual parsing for query-string filter values that Minimal API's implicit binding
/// would otherwise fail on with a generic, unhelpful 400. Every fleet filter that isn't a
/// plain string goes through here so an invalid value comes back as a specific,
/// field-attributed <see cref="Microsoft.AspNetCore.Http.HttpResults.ValidationProblem"/> entry
/// instead of an opaque model-binding failure.
/// </summary>
internal static class QueryParsing
{
    /// <summary>
    /// Parses an optional enum filter value. A missing/blank value is treated as "no
    /// filter" and succeeds with a null result; anything present that doesn't match a
    /// defined member of <typeparamref name="TEnum"/> fails with a descriptive message.
    /// </summary>
    public static bool TryParseEnum<TEnum>(string? value, out TEnum? result, [NotNullWhen(false)] out string? error)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            error = null;
            return true;
        }

        if (Enum.TryParse(value, ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed))
        {
            result = parsed;
            error = null;
            return true;
        }

        result = null;
        error = $"'{value}' is not a valid {typeof(TEnum).Name}. Expected one of: {string.Join(", ", Enum.GetNames<TEnum>())}.";
        return false;
    }

    /// <summary>
    /// Parses an optional <see cref="DateOnly"/> filter value in ISO 8601 (<c>yyyy-MM-dd</c>)
    /// format. A missing/blank value is treated as "no filter" and succeeds with a null result.
    /// </summary>
    public static bool TryParseDateOnly(string? value, out DateOnly? result, [NotNullWhen(false)] out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            error = null;
            return true;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
        {
            result = parsed;
            error = null;
            return true;
        }

        result = null;
        error = $"'{value}' is not a valid date. Expected format: yyyy-MM-dd.";
        return false;
    }
}

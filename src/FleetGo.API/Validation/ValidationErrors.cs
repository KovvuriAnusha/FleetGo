namespace FleetGo.API.Validation;

/// <summary>
/// Accumulates field-level validation failures for a single request so an endpoint can
/// report every problem at once via <c>TypedResults.ValidationProblem</c>, instead of
/// failing fast on the first bad field.
/// </summary>
internal sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public bool HasErrors => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out List<string>? messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        messages.Add(message);
    }

    /// <summary>Adds <paramref name="message"/> under <paramref name="field"/> only when <paramref name="condition"/> is true.</summary>
    public void AddIf(bool condition, string field, string message)
    {
        if (condition)
        {
            Add(field, message);
        }
    }

    /// <summary>
    /// Adds a length-limit failure under <paramref name="field"/> when <paramref name="value"/>
    /// exceeds <paramref name="maxLength"/>. Mirrors the corresponding EF Core
    /// <c>HasMaxLength</c> constraint so an over-length value is rejected here, as a clean
    /// 400, instead of surfacing as an unhandled failure when the database enforces the
    /// column length. A null value never triggers this - pair with <see cref="Add"/> for a
    /// separate required-field check when the field is also mandatory.
    /// </summary>
    public void AddIfTooLong(string? value, int maxLength, string field)
    {
        if (value is not null && value.Length > maxLength)
        {
            Add(field, $"{field} must be {maxLength} characters or fewer.");
        }
    }

    /// <summary>Shapes the accumulated errors for <c>TypedResults.ValidationProblem</c>.</summary>
    public Dictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
}

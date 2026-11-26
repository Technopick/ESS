using System.Globalization;
using System.Text.Json;

namespace Ess.Integration.Nav.Mapping;

/// <summary>
/// NAV Data Mapping Engine (spec §3, component 6). Reads NAV rows through a per-instance field map,
/// so customer-specific field names (e.g. "Job_Title" vs "jobTitle" vs a custom "HR_Grade") are configuration, not code.
/// </summary>
public sealed class NavFieldMapper
{
    private readonly IReadOnlyDictionary<string, string> _map;

    public NavFieldMapper(IReadOnlyDictionary<string, string> defaults, string? overrideJson)
    {
        var merged = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(overrideJson))
        {
            var overrides = JsonSerializer.Deserialize<Dictionary<string, string>>(overrideJson)
                            ?? new Dictionary<string, string>();
            foreach (var (k, v) in overrides) merged[k] = v;
        }
        _map = merged;
    }

    public string NavField(string essField) => _map.TryGetValue(essField, out var f) ? f : essField;

    public string? GetString(JsonElement row, string essField)
    {
        if (!TryGet(row, essField, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => NullIfEmpty(v.GetString()),
            _ => v.ToString()
        };
    }

    public string RequireString(JsonElement row, string essField) =>
        GetString(row, essField) ?? throw new FormatException($"NAV row is missing required field '{NavField(essField)}' (ESS '{essField}').");

    public decimal GetDecimal(JsonElement row, string essField)
    {
        if (!TryGet(row, essField, out var v)) return 0m;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) => d,
            _ => 0m
        };
    }

    public bool? GetBool(JsonElement row, string essField) =>
        TryGet(row, essField, out var v) ? v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(v.GetString(), out var b) ? b : null,
            _ => null
        } : null;

    /// <summary>NAV uses 0001-01-01 as its blank date; treat it as null.</summary>
    public DateOnly? GetDate(JsonElement row, string essField)
    {
        var s = GetString(row, essField);
        if (s is null || s.StartsWith("0001-01-01")) return null;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
            return DateOnly.FromDateTime(dt);
        // OData v3 verbose: /Date(1700000000000)/
        if (s.StartsWith("/Date(") && long.TryParse(s[6..s.IndexOfAny(new[] { ')', '+', '-' }, 6)], out var ms))
            return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime);
        return null;
    }

    public DateTime? GetDateTimeUtc(JsonElement row, string essField)
    {
        var s = GetString(row, essField);
        if (s is null || s.StartsWith("0001-01-01")) return null;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt) ? dt : null;
    }

    private bool TryGet(JsonElement row, string essField, out JsonElement value)
    {
        var nav = NavField(essField);
        if (row.TryGetProperty(nav, out value)) return true;
        // OData field names replace spaces/periods with underscores; tolerate both spellings.
        var alt = nav.Replace(' ', '_').Replace('.', '_');
        return row.TryGetProperty(alt, out value);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}

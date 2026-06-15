using System.Globalization;
using System.Text.RegularExpressions;

namespace NzbWebDAV.Utils;

public static partial class DurationUtil
{
    [GeneratedRegex(@"^\s*(\d+)\s*([smhd])\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DurationRegex();

    public static TimeSpan Parse(string? value, TimeSpan? fallback = null)
    {
        var def = fallback ?? TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(value)) return def;
        var m = DurationRegex().Match(value);
        if (!m.Success) return def;
        var n = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "s" => TimeSpan.FromSeconds(n),
            "m" => TimeSpan.FromMinutes(n),
            "h" => TimeSpan.FromHours(n),
            "d" => TimeSpan.FromDays(n),
            _ => def,
        };
    }
}

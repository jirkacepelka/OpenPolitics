using System.Globalization;
using System.Text;
using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Presentation;

/// <summary>Looks up texts from the packs' localization tables: chosen language, then English, then the key itself.</summary>
public sealed class Localizer
{
    private readonly IReadOnlyDictionary<string, string> _primary;
    private readonly IReadOnlyDictionary<string, string> _fallback;
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    public Localizer(PackSet packs, string language)
    {
        _primary = packs.Localization.TryGetValue(language, out var p) ? p : Empty;
        _fallback = packs.Localization.TryGetValue("en", out var e) ? e : Empty;
    }

    public string T(string key) =>
        _primary.TryGetValue(key, out var v) ? v : _fallback.TryGetValue(key, out var f) ? f : key;

    public string T(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, T(key), args);

    public string Name(string id) => T("name." + id);

    public string? Description(string id) =>
        _primary.TryGetValue("desc." + id, out var v) ? v : _fallback.TryGetValue("desc." + id, out var f) ? f : null;

    /// <summary>Lower-cases and removes Czech diacritics so players can type commands and names without them.</summary>
    public static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var raw in s)
        {
            char c = char.ToLowerInvariant(raw);
            sb.Append(c switch
            {
                'á' or 'ä' or 'â' => 'a', 'č' => 'c', 'ď' => 'd', 'é' or 'ě' or 'ë' => 'e', 'í' => 'i', 'ň' => 'n',
                'ó' or 'ö' => 'o', 'ř' => 'r', 'š' => 's', 'ť' => 't', 'ú' or 'ů' or 'ü' => 'u', 'ý' => 'y', 'ž' => 'z',
                _ => c,
            });
        }
        return sb.ToString();
    }
}

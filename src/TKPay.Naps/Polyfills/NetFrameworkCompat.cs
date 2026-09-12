using System.IO;

namespace TKPay.Naps.Polyfills;

/// <summary>
/// Polyfills for APIs missing on .NET Framework 4.8.
/// </summary>
internal static class NetFrameworkCompat
{
#if !NET5_0_OR_GREATER
    /// <summary>
    /// Polyfill for Dictionary.GetValueOrDefault which is unavailable on net48.
    /// </summary>
    public static TValue? GetValueOrDefault<TKey, TValue>(
        this Dictionary<TKey, TValue> dict, TKey key, TValue? defaultValue = default)
        where TKey : notnull
    {
        return dict.TryGetValue(key, out var value) ? value : defaultValue;
    }

    /// <summary>
    /// Polyfill for IReadOnlyDictionary.GetValueOrDefault which is unavailable on net48.
    /// </summary>
    public static TValue? GetValueOrDefault<TKey, TValue>(
        this IReadOnlyDictionary<TKey, TValue> dict, TKey key, TValue? defaultValue = default)
        where TKey : notnull
    {
        return dict.TryGetValue(key, out var value) ? value : defaultValue;
    }

    /// <summary>
    /// Polyfill for string.Contains(string, StringComparison) which is unavailable on net48.
    /// </summary>
    public static bool Contains(this string source, string value, StringComparison comparison)
    {
        return source.IndexOf(value, comparison) >= 0;
    }
#endif
}

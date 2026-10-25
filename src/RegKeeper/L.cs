using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using RegKeeper.Core;

namespace RegKeeper;

/// <summary>
/// Interface language. The Russian text in the code is the key; Data\en.json maps it to English, a missing entry
/// falls back to Russian. Interpolated keys use holes: L.T($"Удалить {n} шт.") looks up "Удалить {0} шт.".
/// The choice lives in HKCU\Software\RegKeeper\Language (RK_LANG overrides it) and applies after a restart.
/// No WPF in this half: the watcher compiles it on its own; translating views is in L.Ui.cs.
/// </summary>
public static partial class L
{
    public const string Auto = "Auto", Ru = "ru", En = "en";
    const string Key = @"HKEY_CURRENT_USER\Software\RegKeeper";
    public static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "Data", "en.json");

    public static string Saved
    {
        get => Reg.GetString(Key, "Language") is { } l && l is Ru or En ? l : Auto;
        set => Registry.SetValue(Key, "Language", value);
    }

    static string Resolve(string mode) =>
        mode is Ru or En ? mode : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "ru" or "uk" or "be" or "kk" ? Ru : En;

    static string? _current;
    /// <summary>The language in use; settable so tests can switch it.</summary>
    public static string Current
    {
        get => _current ??= Resolve(Environment.GetEnvironmentVariable("RK_LANG") is { Length: > 0 } e ? e : Saved);
        set => _current = value;
    }

    static readonly Lazy<Dictionary<string, string>> English = new(() =>
        File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath))! : new());

    public static IReadOnlyDictionary<string, string> Dictionary => English.Value;

    public static string T(string ru) => Current == En && English.Value.TryGetValue(ru, out var en) ? en : ru;

    public static string T(Text text) => text.Resolve();

    /// <summary>Builds the lookup key of an interpolated string: holes become {0}, {1}...</summary>
    [InterpolatedStringHandler]
    public ref struct Text
    {
        readonly StringBuilder _key;
        readonly List<string> _args;

        public Text(int literalLength, int formattedCount)
        {
            _key = new StringBuilder(literalLength + formattedCount * 3);
            _args = new List<string>(formattedCount);
        }

        public void AppendLiteral(string s) => _key.Append(s);

        public void AppendFormatted<T>(T value, string? format = null)
        {
            _key.Append('\u0001').Append(_args.Count).Append('\u0002');
            _args.Add(value is IFormattable f ? f.ToString(format, CultureInfo.CurrentCulture) : value?.ToString() ?? "");
        }

        public void AppendFormatted<T>(T value, int alignment, string? format = null)
        {
            AppendFormatted(value, format);
            _args[^1] = alignment < 0 ? _args[^1].PadRight(-alignment) : _args[^1].PadLeft(alignment);
        }

        // holes are marked with control characters while building, so literal braces never need escaping
        internal string Resolve()
        {
            var key = _key.ToString().Replace("\u0001", "{").Replace("\u0002", "}");
            var args = _args;
            return System.Text.RegularExpressions.Regex.Replace(T(key), @"\{(\d+)\}",
                m => int.Parse(m.Groups[1].Value) is var i && i < args.Count ? args[i] : m.Value);
        }
    }
}

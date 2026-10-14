using System.Globalization;
using RegKeeper.Core;

namespace RegKeeper.Modules;

/// <summary>Editable text form of a registry value and back. DWORD/QWORD: decimal or 0x-hex; binary-ish types: hex bytes.</summary>
public static class ValueText
{
    public static bool IsText(uint type) => type is RawValue.Sz or RawValue.ExpandSz or RawValue.Link;

    public static string ToText(RawValue v) => v.Type switch
    {
        _ when IsText(v.Type) => v.AsString(),
        RawValue.MultiSz => string.Join("\r\n", v.AsMulti()),
        RawValue.Dword when v.Data.Length == 4 => BitConverter.ToUInt32(v.Data).ToString(),
        RawValue.Qword when v.Data.Length == 8 => BitConverter.ToUInt64(v.Data).ToString(),
        _ => Convert.ToHexString(v.Data).Chunk(2).Aggregate("", (s, c) => s + (s == "" ? "" : " ") + new string(c)),
    };

    /// <summary>Throws FormatException/OverflowException on bad input.</summary>
    public static RawValue Parse(string name, uint type, string text) => type switch
    {
        _ when IsText(type) => RawValue.String(name, text, type),
        RawValue.MultiSz => RawValue.Multi(name, text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l != "")),
        RawValue.Dword => RawValue.FromDword(name, (uint)Number(text, uint.MaxValue)),
        RawValue.Qword => RawValue.FromQword(name, Number(text, ulong.MaxValue)),
        _ => new RawValue(name, type, Convert.FromHexString(new string(text.Where(char.IsAsciiHexDigit).ToArray()))),
    };

    static ulong Number(string text, ulong max)
    {
        var t = text.Trim();
        var v = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.Parse(t[2..], NumberStyles.HexNumber)
            : ulong.Parse(t);
        return v <= max ? v : throw new OverflowException("Слишком большое число");
    }
}

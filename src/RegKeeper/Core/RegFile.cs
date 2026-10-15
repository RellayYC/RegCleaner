using System.Globalization;
using System.Text;

namespace RegKeeper.Core;

public enum RegOpKind { CreateKey, DeleteKey, SetValue, DeleteValue }

public sealed record RegOp(RegOpKind Kind, string Key, RawValue? Value = null, string? ValueName = null);

/// <summary>.reg files (regedit "Version 5.00" format, UTF-16 LE). Writer is lossless: anything that isn't a clean
/// string or dword goes out as hex(n).</summary>
public static class RegFile
{
    public const string Header = "Windows Registry Editor Version 5.00";

    public static string Export(IEnumerable<string> keys, bool recursive = true)
    {
        var sb = new StringBuilder(Header).Append("\r\n");
        foreach (var k in keys) AppendKey(sb, Reg.Normalize(k), recursive);
        return sb.ToString();
    }

    public static void AppendKey(StringBuilder sb, string path, bool recursive)
    {
        using var k = Reg.Open(path);
        if (k == null) return;
        sb.Append("\r\n[").Append(path).Append("]\r\n");
        // sorted so exports are deterministic (registry enumeration order carries no meaning)
        foreach (var v in Reg.Values(k).OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)) sb.Append(FormatValue(v)).Append("\r\n");
        if (recursive)
            foreach (var sub in Reg.SubKeys(k).Order(StringComparer.OrdinalIgnoreCase)) AppendKey(sb, path + "\\" + sub, true);
    }

    public static void Save(string file, string text) => File.WriteAllText(file, text, Encoding.Unicode);

    public static string FormatName(string name) => name == "" ? "@" : "\"" + Escape(name) + "\"";

    // \r \n \0 escapes are ours: regedit never emits them (it can't store such names at all)
    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0");

    public static string FormatValue(RawValue v)
    {
        var name = FormatName(v.Name);
        if (v.Type == RawValue.Sz && IsCleanString(v.Data, out var s)) return $"{name}=\"{Escape(s)}\"";
        if (v.Type == RawValue.Dword && v.Data.Length == 4) return $"{name}=dword:{BitConverter.ToUInt32(v.Data):x8}";
        var prefix = v.Type == RawValue.Binary ? $"{name}=hex:" : $"{name}=hex({v.Type:x}):";
        return prefix + WrapHex(v.Data, prefix.Length);
    }

    /// <summary>True when data is UTF-16 text with exactly one terminating NUL and nothing that breaks a .reg line.</summary>
    static bool IsCleanString(byte[] data, out string s)
    {
        s = "";
        if (data.Length < 2 || data.Length % 2 != 0) return false;
        var all = Encoding.Unicode.GetString(data);
        if (all[^1] != '\0') return false;
        s = all[..^1];
        return s.IndexOfAny(new[] { '\0', '\r', '\n' }) < 0;
    }

    static string WrapHex(byte[] data, int firstLineUsed)
    {
        var sb = new StringBuilder();
        var col = firstLineUsed;
        for (var i = 0; i < data.Length; i++)
        {
            var piece = data[i].ToString("x2") + (i < data.Length - 1 ? "," : "");
            if (col + piece.Length > 78 && i < data.Length - 1)
            {
                sb.Append("\\\r\n  ");
                col = 2;
            }
            sb.Append(piece);
            col += piece.Length;
        }
        return sb.ToString();
    }

    public static List<RegOp> Parse(string text)
    {
        var ops = new List<RegOp>();
        string? key = null;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            // hex data continues on the next line after a trailing backslash
            while (line.EndsWith('\\') && line.Contains("=hex", StringComparison.OrdinalIgnoreCase) && i + 1 < lines.Length)
                line = line[..^1] + lines[++i].Trim();
            if (line == "" || line.StartsWith(';') || line.StartsWith(Header) || line == "REGEDIT4") continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var path = line[1..^1];
                if (path.StartsWith('-'))
                {
                    ops.Add(new RegOp(RegOpKind.DeleteKey, Reg.Normalize(path[1..])));
                    key = null;
                }
                else
                {
                    key = Reg.Normalize(path);
                    ops.Add(new RegOp(RegOpKind.CreateKey, key));
                }
                continue;
            }

            if (key == null) continue;
            var (name, rest) = ParseName(line);
            if (rest == "-") ops.Add(new RegOp(RegOpKind.DeleteValue, key, ValueName: name));
            else ops.Add(new RegOp(RegOpKind.SetValue, key, ParseData(name, rest)));
        }
        return ops;
    }

    static (string Name, string Data) ParseName(string line)
    {
        if (line.StartsWith("@=")) return ("", line[2..]);
        if (!line.StartsWith('"')) throw new FormatException(L.T("Ожидалось имя значения: ") + line);
        var (name, end) = ReadQuoted(line, 0);
        if (end >= line.Length || line[end] != '=') throw new FormatException(L.T("Ожидалось '=': ") + line);
        return (name, line[(end + 1)..]);
    }

    /// <summary>Reads a "quoted" string starting at <paramref name="start"/>, returns the text and the index after the closing quote.</summary>
    static (string, int) ReadQuoted(string s, int start)
    {
        var sb = new StringBuilder();
        for (var i = start + 1; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
                sb.Append(s[++i] switch { 'r' => '\r', 'n' => '\n', '0' => '\0', var c => c });
            else if (s[i] == '"') return (sb.ToString(), i + 1);
            else sb.Append(s[i]);
        }
        throw new FormatException(L.T("Незакрытая строка: ") + s);
    }

    static RawValue ParseData(string name, string rest)
    {
        if (rest.StartsWith('"')) return RawValue.String(name, ReadQuoted(rest, 0).Item1);
        if (rest.StartsWith("dword:", StringComparison.OrdinalIgnoreCase))
            return RawValue.FromDword(name, uint.Parse(rest[6..], NumberStyles.HexNumber));
        uint type = RawValue.Binary;
        string hex;
        if (rest.StartsWith("hex:", StringComparison.OrdinalIgnoreCase)) hex = rest[4..];
        else if (rest.StartsWith("hex(", StringComparison.OrdinalIgnoreCase))
        {
            var close = rest.IndexOf("):", StringComparison.Ordinal);
            type = uint.Parse(rest[4..close], NumberStyles.HexNumber);
            hex = rest[(close + 2)..];
        }
        else throw new FormatException(L.T("Неизвестный формат данных: ") + rest);
        var bytes = hex.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(b => byte.Parse(b, NumberStyles.HexNumber)).ToArray();
        return new RawValue(name, type, bytes);
    }

    /// <summary>Applies parsed ops through an undo transaction (so an import can be rolled back).</summary>
    public static void Apply(IEnumerable<RegOp> ops, UndoTx tx)
    {
        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case RegOpKind.CreateKey: tx.CreateKey(op.Key); break;
                case RegOpKind.DeleteKey: tx.DeleteKey(op.Key); break;
                case RegOpKind.SetValue: tx.SetValue(op.Key, op.Value!); break;
                case RegOpKind.DeleteValue: tx.DeleteValue(op.Key, op.ValueName!); break;
            }
        }
    }

    /// <summary>Applies ops directly, no backup. Used by undo restore itself.</summary>
    public static void ApplyRaw(IEnumerable<RegOp> ops)
    {
        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case RegOpKind.CreateKey: Reg.Create(op.Key).Dispose(); break;
                case RegOpKind.DeleteKey: if (Reg.Exists(op.Key)) Reg.DeleteTree(op.Key); break;
                case RegOpKind.SetValue:
                    using (var k = Reg.Create(op.Key)) Reg.Write(k, op.Value!);
                    break;
                case RegOpKind.DeleteValue:
                    using (var k = Reg.Open(op.Key, true)) k?.DeleteValue(op.ValueName!, false);
                    break;
            }
        }
    }
}

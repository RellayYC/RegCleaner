using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace RegKeeper.Core;

/// <summary>Raw registry value: type number + bytes exactly as stored. Lossless for every REG_* type.</summary>
public sealed record RawValue(string Name, uint Type, byte[] Data)
{
    public const uint None = 0, Sz = 1, ExpandSz = 2, Binary = 3, Dword = 4, DwordBE = 5, Link = 6, MultiSz = 7, Qword = 11;

    public static RawValue String(string name, string s, uint type = Sz) => new(name, type, Encoding.Unicode.GetBytes(s + "\0"));
    public static RawValue FromDword(string name, uint v) => new(name, Dword, BitConverter.GetBytes(v));
    public static RawValue FromQword(string name, ulong v) => new(name, Qword, BitConverter.GetBytes(v));
    public static RawValue Multi(string name, IEnumerable<string> lines) =>
        new(name, MultiSz, Encoding.Unicode.GetBytes(string.Concat(lines.Select(l => l + "\0")) + "\0"));

    public string AsString() => Encoding.Unicode.GetString(Data).TrimEnd('\0');
    public string[] AsMulti() => AsString() is var s && s == "" ? Array.Empty<string>() : s.Split('\0');

    public string TypeName => Type switch
    {
        None => "REG_NONE", Sz => "REG_SZ", ExpandSz => "REG_EXPAND_SZ", Binary => "REG_BINARY", Dword => "REG_DWORD",
        DwordBE => "REG_DWORD_BIG_ENDIAN", Link => "REG_LINK", MultiSz => "REG_MULTI_SZ", 8 => "REG_RESOURCE_LIST",
        9 => "REG_FULL_RESOURCE_DESCRIPTOR", 10 => "REG_RESOURCE_REQUIREMENTS_LIST", Qword => "REG_QWORD", _ => $"0x{Type:x}"
    };

    public string Display() => Type switch
    {
        Sz or ExpandSz or Link => AsString(),
        MultiSz => string.Join(" | ", AsMulti()),
        Dword when Data.Length == 4 => $"0x{BitConverter.ToUInt32(Data):x8} ({BitConverter.ToUInt32(Data)})",
        Qword when Data.Length == 8 => $"0x{BitConverter.ToUInt64(Data):x16} ({BitConverter.ToUInt64(Data)})",
        _ => Data.Length > 64 ? Convert.ToHexString(Data, 0, 64) + "..." : Convert.ToHexString(Data)
    };

    public bool SameAs(RawValue? o) => o != null && o.Type == Type && o.Data.AsSpan().SequenceEqual(Data);
}

/// <summary>Registry helpers. Paths are full ("HKEY_LOCAL_MACHINE\SOFTWARE\...") and always opened in the 64-bit view;
/// 32-bit data is addressed explicitly via WOW6432Node.</summary>
public static class Reg
{
    static readonly (string Long, string Short, RegistryHive Hive)[] Hives =
    {
        ("HKEY_LOCAL_MACHINE", "HKLM", RegistryHive.LocalMachine),
        ("HKEY_CURRENT_USER", "HKCU", RegistryHive.CurrentUser),
        ("HKEY_CLASSES_ROOT", "HKCR", RegistryHive.ClassesRoot),
        ("HKEY_USERS", "HKU", RegistryHive.Users),
        ("HKEY_CURRENT_CONFIG", "HKCC", RegistryHive.CurrentConfig),
    };

    public static readonly string[] RootNames = Hives.Select(h => h.Long).ToArray();

    /// <summary>Normalizes "HKLM\x", "Computer\HKEY_LOCAL_MACHINE\x", trailing slashes to the canonical long form.</summary>
    public static string Normalize(string path)
    {
        path = path.Trim().Trim('\\');
        if (path.StartsWith("Computer\\", StringComparison.OrdinalIgnoreCase)) path = path[9..];
        if (path.StartsWith("Компьютер\\", StringComparison.OrdinalIgnoreCase)) path = path[10..];
        var i = path.IndexOf('\\');
        var root = i < 0 ? path : path[..i];
        foreach (var h in Hives)
            if (root.Equals(h.Long, StringComparison.OrdinalIgnoreCase) || root.Equals(h.Short, StringComparison.OrdinalIgnoreCase))
                return h.Long + (i < 0 ? "" : path[i..]);
        throw new ArgumentException("Неизвестный корень реестра: " + root);
    }

    public static string Short(string path)
    {
        foreach (var h in Hives)
            if (path.StartsWith(h.Long, StringComparison.OrdinalIgnoreCase)) return h.Short + path[h.Long.Length..];
        return path;
    }

    public static (RegistryKey Root, string Sub) Split(string path)
    {
        path = Normalize(path);
        var i = path.IndexOf('\\');
        var root = i < 0 ? path : path[..i];
        var hive = Hives.First(h => h.Long.Equals(root, StringComparison.OrdinalIgnoreCase)).Hive;
        return (RegistryKey.OpenBaseKey(hive, RegistryView.Registry64), i < 0 ? "" : path[(i + 1)..]);
    }

    public static RegistryKey? Open(string path, bool write = false)
    {
        var (root, sub) = Split(path);
        if (sub == "") return root;
        try { return root.OpenSubKey(sub, write); }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    public static RegistryKey Create(string path)
    {
        var (root, sub) = Split(path);
        return sub == "" ? root : root.CreateSubKey(sub, true);
    }

    public static bool Exists(string path)
    {
        using var k = Open(path);
        return k != null;
    }

    public static string Combine(string a, string b) => b == "" ? a : a.TrimEnd('\\') + "\\" + b;
    public static string Parent(string path) => path.Contains('\\') ? path[..path.LastIndexOf('\\')] : "";
    public static string Leaf(string path) => path.Contains('\\') ? path[(path.LastIndexOf('\\') + 1)..] : path;

    public static string[] SubKeys(RegistryKey k)
    {
        try { return k.GetSubKeyNames(); } catch { return Array.Empty<string>(); }
    }

    public static string[] SubKeys(string path)
    {
        using var k = Open(path);
        return k == null ? Array.Empty<string>() : SubKeys(k);
    }

    public static string[] ValueNames(RegistryKey k)
    {
        try { return k.GetValueNames(); } catch { return Array.Empty<string>(); }
    }

    public static IEnumerable<RawValue> Values(RegistryKey k)
    {
        foreach (var n in ValueNames(k))
            if (Read(k, n) is { } v) yield return v;
    }

    public static string? GetString(RegistryKey k, string name)
    {
        // some REG_SZ (hivelist...) carry extra trailing NULs that .NET keeps
        try { return k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString()?.TrimEnd('\0'); }
        catch { return null; }
    }

    public static string? GetString(string path, string name)
    {
        using var k = Open(path);
        return k == null ? null : GetString(k, name);
    }

    public static RawValue? Read(RegistryKey k, string name)
    {
        SafeRegistryHandle h;
        try { h = k.Handle; } // throws on some protected keys (e.g. under HKLM\SECURITY)
        catch (UnauthorizedAccessException) { return null; }
        uint size = 0;
        var rc = RegQueryValueExW(h, name, IntPtr.Zero, out var type, null, ref size);
        if (rc != 0 && rc != ErrorMoreData) return null;
        while (true)
        {
            var buf = new byte[size];
            rc = RegQueryValueExW(h, name, IntPtr.Zero, out type, buf, ref size);
            if (rc == ErrorMoreData) continue;
            if (rc != 0) return null;
            if (size != buf.Length) Array.Resize(ref buf, (int)size);
            return new RawValue(name, type, buf);
        }
    }

    public static RawValue? Read(string path, string name)
    {
        using var k = Open(path);
        return k == null ? null : Read(k, name);
    }

    public static void Write(RegistryKey k, RawValue v)
    {
        var rc = RegSetValueExW(k.Handle, v.Name, 0, v.Type, v.Data, v.Data.Length);
        if (rc != 0) throw new IOException($"RegSetValueEx({v.Name}) = {rc}");
    }

    public static void DeleteTree(string path)
    {
        var (root, sub) = Split(path);
        if (sub == "") throw new InvalidOperationException("Нельзя удалить корень реестра");
        root.DeleteSubKeyTree(sub, false);
    }

    const int ErrorMoreData = 234;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegQueryValueExW(SafeRegistryHandle h, string? name, IntPtr reserved, out uint type, byte[]? data, ref uint size);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    static extern int RegSetValueExW(SafeRegistryHandle h, string? name, int reserved, uint type, byte[] data, int size);
}

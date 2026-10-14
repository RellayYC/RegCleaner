using System.IO.Enumeration;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public sealed record FileKey(string Dir, string[] Patterns, bool Recurse, bool RemoveSelf);

/// <summary>One cleanup entry in winapp2.ini terms (our built-ins use the same syntax).</summary>
public sealed class CleanRule
{
    public string Name { get; init; } = "";
    public string Category { get; set; } = "";
    public string? Warning { get; set; }
    public bool Checked { get; set; }
    public bool DefaultOn { get; set; }
    public bool RecycleBin { get; set; }
    public List<string> Detect { get; } = new();
    public List<string> DetectFile { get; } = new();
    public List<FileKey> Files { get; } = new();
    public List<string> Excludes { get; } = new();
    public List<string> RegKeys { get; } = new();

    public bool Analyzed { get; set; }
    public List<Leftover> Found { get; set; } = new();
    public long Size { get; set; }
    public long Count { get; set; }
    public string SizeText => Analyzed ? Sys.Size(Size) : "";
    public string CountText => Analyzed ? Count.ToString() : "";
}

/// <summary>
/// winapp2.ini (CCleaner-format cleanup database, CC-BY-SA, github.com/MoscaDotTo/Winapp2) parser and executor.
/// Only files matching FileKey patterns are deleted; REMOVESELF folders go only once they are empty.
/// </summary>
public static partial class Winapp2
{
    public const string Url = "https://raw.githubusercontent.com/MoscaDotTo/Winapp2/master/Winapp2.ini";
    public static readonly string IniPath = Path.Combine(AppPaths.Data, "winapp2.ini");
    public static readonly string IgnorePath = Path.Combine(AppPaths.Data, "cleanup_ignore.txt");
    static readonly string CheckedPath = Path.Combine(AppPaths.Data, "cleanup_checked.txt");

    const string Builtin = """
        [Временные файлы пользователя]
        Section=Windows
        Default=1
        FileKey1=%Temp%|*
        FileKey2=%Temp%\*|*|REMOVESELF
        [Временные файлы Windows]
        Section=Windows
        Default=1
        FileKey1=%WinDir%\Temp|*
        FileKey2=%WinDir%\Temp\*|*|REMOVESELF
        [Загрузки Windows Update]
        Section=Windows
        Default=1
        FileKey1=%WinDir%\SoftwareDistribution\Download|*
        FileKey2=%WinDir%\SoftwareDistribution\Download\*|*|REMOVESELF
        [Дампы памяти]
        Section=Windows
        Default=1
        FileKey1=%WinDir%|MEMORY.DMP
        FileKey2=%WinDir%\Minidump|*.dmp
        FileKey3=%LocalAppData%\CrashDumps|*.dmp
        [Отчёты об ошибках Windows]
        Section=Windows
        Default=1
        FileKey1=%ProgramData%\Microsoft\Windows\WER\ReportArchive\*|*|REMOVESELF
        FileKey2=%ProgramData%\Microsoft\Windows\WER\ReportQueue\*|*|REMOVESELF
        FileKey3=%LocalAppData%\Microsoft\Windows\WER\ReportArchive\*|*|REMOVESELF
        FileKey4=%LocalAppData%\Microsoft\Windows\WER\ReportQueue\*|*|REMOVESELF
        [Кэш эскизов и значков]
        Section=Windows
        FileKey1=%LocalAppData%\Microsoft\Windows\Explorer|thumbcache_*.db;iconcache_*.db
        [Корзина]
        Section=Windows
        RecycleBin=1
        """;

    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    static readonly string Pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    static readonly string Win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    /// <summary>winapp2 variables. %ProgramFiles% and %CommonProgramFiles% mean both bitness folders.</summary>
    static readonly Dictionary<string, string[]> Vars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["%LocalAppData%"] = new[] { Local },
        ["%AppData%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) },
        ["%LocalLowAppData%"] = new[] { Path.Combine(Path.GetDirectoryName(Local)!, "LocalLow") },
        ["%ProgramFiles%"] = new[] { Pf, Pf86 },
        ["%CommonProgramFiles%"] = new[] { Path.Combine(Pf, "Common Files"), Path.Combine(Pf86, "Common Files") },
        ["%ProgramData%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) },
        ["%UserProfile%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) },
        ["%WinDir%"] = new[] { Win },
        ["%SystemDrive%"] = new[] { Path.GetPathRoot(Win)!.TrimEnd('\\') },
        ["%Public%"] = new[] { Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public" },
        ["%Temp%"] = new[] { Path.GetTempPath().TrimEnd('\\') },
        ["%Documents%"] = new[] { Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
        ["%UserName%"] = new[] { Environment.UserName },
    };

    static readonly Dictionary<string, string> LangSec = new()
    {
        ["3021"] = "Приложения", ["3022"] = "Интернет", ["3023"] = "Мультимедиа", ["3024"] = "Утилиты", ["3025"] = "Windows",
        ["3026"] = "Firefox", ["3029"] = "Google Chrome", ["3030"] = "Thunderbird", ["3006"] = "Microsoft Edge", ["3036"] = "Мультимедиа",
    };

    static readonly EnumerationOptions Shallow = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
    static readonly EnumerationOptions Deep = new() { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, RecurseSubdirectories = true };

    public static List<CleanRule> Parse(string text)
    {
        var rules = new List<CleanRule>();
        CleanRule? r = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line == "" || line.StartsWith(';')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                rules.Add(r = new CleanRule { Name = line[1..^1].TrimEnd(' ', '*') });
                continue;
            }
            var eq = line.IndexOf('=');
            if (r == null || eq < 0) continue;
            var val = line[(eq + 1)..].Trim();
            switch (line[..eq].TrimEnd("0123456789".ToCharArray()).ToLowerInvariant())
            {
                case "detect": r.Detect.Add(val); break;
                case "detectfile": r.DetectFile.Add(val); break;
                case "filekey": r.Files.Add(ParseFileKey(val)); break;
                case "excludekey": r.Excludes.Add(val); break;
                case "regkey": r.RegKeys.Add(val); break;
                case "warning": r.Warning = val; break;
                case "section": r.Category = val; break;
                case "langsecref" when r.Category == "": r.Category = LangSec.GetValueOrDefault(val, "Браузеры"); break;
                case "default": r.DefaultOn = val == "1"; break;
                case "recyclebin": r.RecycleBin = val == "1"; break;
            }
        }
        foreach (var x in rules.Where(x => x.Category == "")) x.Category = "Прочее";
        return rules;
    }

    static FileKey ParseFileKey(string val)
    {
        var p = val.Split('|');
        var flags = p.Length > 2 ? p[2].ToUpperInvariant() : "";
        var patterns = p.Length > 1 && p[1] != "" ? p[1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : new[] { "*" };
        return new FileKey(p[0].TrimEnd('\\'), patterns, flags.Contains("RECURSE"), flags.Contains("REMOVESELF"));
    }

    /// <summary>Built-ins plus winapp2.ini (if downloaded), only the entries that apply to this PC, with saved ticks.</summary>
    public static List<CleanRule> Load()
    {
        var rules = Parse(Builtin);
        if (File.Exists(IniPath)) rules.AddRange(Parse(File.ReadAllText(IniPath)));
        var saved = File.Exists(CheckedPath) ? File.ReadAllLines(CheckedPath).ToHashSet() : null;
        rules = rules.Where(Applies).ToList();
        foreach (var r in rules) r.Checked = saved?.Contains(r.Name) ?? r.DefaultOn;
        return rules;
    }

    public static void SaveChecked(IEnumerable<CleanRule> rules) =>
        File.WriteAllLines(CheckedPath, rules.Where(r => r.Checked).Select(r => r.Name));

    public static async Task<int> Download()
    {
        using var http = new HttpClient();
        var text = await http.GetStringAsync(Url);
        var count = Parse(text).Count;
        if (count < 500) throw new InvalidDataException($"Загруженный файл не похож на winapp2.ini (правил: {count}).");
        await File.WriteAllTextAsync(IniPath, text);
        return count;
    }

    /// <summary>No detection keys = always applies; otherwise any Detect key or DetectFile path must exist.</summary>
    public static bool Applies(CleanRule r) =>
        r.RecycleBin || r.Detect.Count + r.DetectFile.Count == 0
        || r.Detect.Any(d => Reg.Exists(Reg.Normalize(d)))
        || r.DetectFile.Any(d => ExpandVars(d).Any(p => Glob(p, true).Any()));

    /// <summary>Expands winapp2 variables; multi-valued ones (%ProgramFiles%) produce several paths.</summary>
    public static IEnumerable<string> ExpandVars(string s)
    {
        var m = VarRx().Match(s);
        if (!m.Success) return new[] { s };
        var vals = Vars.TryGetValue(m.Value, out var v) ? v : new[] { Environment.ExpandEnvironmentVariables(m.Value) };
        if (vals[0] == m.Value) return Array.Empty<string>(); // unknown variable: path can't exist
        return vals.Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(x => ExpandVars(s[..m.Index] + x + s[(m.Index + m.Length)..]));
    }

    [GeneratedRegex("%[^%]+%")]
    private static partial Regex VarRx();

    /// <summary>Existing directories (and files, if asked) matching a path with * or ? in any segment.</summary>
    public static List<string> Glob(string pattern, bool files = false)
    {
        var parts = pattern.TrimEnd('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !parts[0].EndsWith(':')) return new();
        var cur = new List<string> { parts[0] + "\\" };
        for (var i = 1; i < parts.Length && cur.Count > 0; i++)
        {
            var seg = parts[i];
            var last = i == parts.Length - 1 && files;
            cur = cur.SelectMany(d => Children(d, seg, last)).ToList();
        }
        return parts.Length == 1 ? cur.Where(Directory.Exists).ToList() : cur;
    }

    static IEnumerable<string> Children(string dir, string seg, bool files)
    {
        if (seg.IndexOfAny(new[] { '*', '?' }) < 0)
        {
            var p = Path.Combine(dir, seg);
            return Directory.Exists(p) || files && File.Exists(p) ? new[] { p } : Array.Empty<string>();
        }
        try
        {
            var entries = files ? Directory.EnumerateFileSystemEntries(dir, "*", Shallow) : Directory.EnumerateDirectories(dir, "*", Shallow);
            return entries.Where(e => FileSystemName.MatchesSimpleExpression(seg, Path.GetFileName(e))).ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>ExcludeKey: "FILE|dir\|name;name" or "PATH|dir\|pattern" (anything below dir). REG excludes are ignored.</summary>
    static List<Regex> ExcludeRegexes(CleanRule r)
    {
        static string W(string s) => Regex.Escape(s).Replace(@"\*", @"[^\\]*").Replace(@"\?", @"[^\\]");
        var list = new List<Regex>();
        foreach (var x in r.Excludes)
        {
            var p = x.Split('|');
            if (p.Length < 2 || p[0].Equals("REG", StringComparison.OrdinalIgnoreCase)) continue;
            var isFile = p[0].Equals("FILE", StringComparison.OrdinalIgnoreCase);
            var dir = p[1].TrimEnd('\\');
            var pat = p.Length > 2 && p[2] != "" ? p[2] : "*";
            if (isFile && p.Length == 2) { pat = Path.GetFileName(dir); dir = Path.GetDirectoryName(dir) ?? dir; }
            var names = string.Join("|", pat.Split(';').Select(W));
            foreach (var d in ExpandVars(dir))
                list.Add(new Regex("^" + W(d) + (isFile ? @"\\" : @"(\\.*)?\\") + "(" + names + ")$", RegexOptions.IgnoreCase));
        }
        return list;
    }

    public static List<string> LoadIgnore() =>
        File.Exists(IgnorePath) ? File.ReadAllLines(IgnorePath).Select(l => l.Trim()).Where(l => l != "" && !l.StartsWith(';')).ToList() : new();

    /// <summary>Never remove a variable root itself (%Temp%, %WinDir%...), only what's inside.</summary>
    static bool IsRoot(string dir) =>
        dir.TrimEnd('\\').Length <= 3 || Vars.Values.SelectMany(v => v).Any(v => v.TrimEnd('\\').Equals(dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

    public static void Analyze(CleanRule r, IReadOnlyList<string> ignore)
    {
        r.Analyzed = true;
        if (r.RecycleBin)
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            SHQueryRecycleBin(null, ref info);
            (r.Size, r.Count) = (info.i64Size, info.i64NumItems);
            return;
        }

        var excl = ExcludeRegexes(r);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<Leftover>();
        var dirs = new List<Leftover>();
        foreach (var fk in r.Files)
            foreach (var dir in ExpandVars(fk.Dir).SelectMany(d => Glob(d)))
            {
                List<FileInfo> list;
                try { list = new DirectoryInfo(dir).EnumerateFiles("*", fk.Recurse || fk.RemoveSelf ? Deep : Shallow).ToList(); }
                catch { continue; }
                foreach (var f in list)
                    if (fk.Patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, f.Name))
                        && !excl.Any(x => x.IsMatch(f.FullName)) && !ignore.Any(i => Leftovers.IsSameOrInside(f.FullName, i))
                        && seen.Add(f.FullName))
                        files.Add(new Leftover { Kind = "Файл", Path = f.FullName, Size = f.Length });
                if (fk.RemoveSelf && !IsRoot(dir) && !ignore.Any(i => Leftovers.IsSameOrInside(dir, i)) && seen.Add(dir))
                    dirs.Add(new Leftover { Kind = "Папка", Path = dir });
            }

        var regs = new List<Leftover>();
        foreach (var rk in r.RegKeys)
        {
            var p = rk.Split('|');
            var key = Reg.Normalize(p[0]);
            if (p.Length > 1 && p[1] != "")
            {
                if (Reg.Read(key, p[1]) != null) regs.Add(new Leftover { Kind = "Значение реестра", Path = key, ValueName = p[1] });
            }
            else if (Reg.Exists(key)) regs.Add(new Leftover { Kind = "Ключ реестра", Path = key });
        }

        r.Found = files.Concat(dirs).Concat(regs).ToList();
        r.Size = files.Sum(f => f.Size);
        r.Count = files.Count + regs.Count;
    }

    /// <summary>Deletes checked items of analyzed rules. Files are deleted for good (it's junk); registry goes through undo.</summary>
    public static (long Freed, int Failed) Clean(IEnumerable<CleanRule> rules, UndoTx tx)
    {
        long freed = 0;
        var failed = 0;
        foreach (var r in rules)
        {
            if (r.RecycleBin)
            {
                if (r.Count > 0 && SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND) == 0) freed += r.Size;
                continue;
            }
            foreach (var l in r.Found.Where(l => l.Checked))
            {
                try
                {
                    switch (l.Kind)
                    {
                        case "Файл":
                            File.SetAttributes(l.Path, FileAttributes.Normal);
                            File.Delete(l.Path);
                            freed += l.Size;
                            break;
                        case "Папка": DeleteEmptyTree(l.Path); break;
                        case "Ключ реестра": tx.DeleteKey(l.Path); break;
                        case "Значение реестра": tx.DeleteValue(l.Path, l.ValueName!); break;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed++; }
            }
            r.Analyzed = false;
            r.Found = new();
        }
        return (freed, failed + tx.Errors.Count);
    }

    /// <summary>Removes empty folders bottom-up; anything still holding a file (locked, excluded, unticked) stays.</summary>
    static void DeleteEmptyTree(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var sub in Directory.EnumerateDirectories(dir, "*", Shallow)) DeleteEmptyTree(sub);
        if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    const uint SHERB_NOCONFIRMATION = 1, SHERB_NOPROGRESSUI = 2, SHERB_NOSOUND = 4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBin(string? root, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);
}

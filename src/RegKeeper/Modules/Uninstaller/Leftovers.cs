using System.Text.RegularExpressions;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public sealed class Leftover
{
    public bool Checked { get; set; } = true;
    public string Kind { get; init; } = ""; // i18n: keep - a data value, shown through KindText
    public string KindText => L.T(Kind);
    public string Path { get; init; } = "";
    public long Size { get; init; }
    public string? ValueName { get; init; }
    public string SizeText => Size > 0 ? Sys.Size(Size) : "";
    public bool IsRegistry => Path.StartsWith("HKEY_");
}

/// <summary>
/// Finds what an uninstaller left behind. Matching is deliberately strict (normalized name equality, never prefixes):
/// a missed folder is an annoyance, a wrongly deleted one is data loss.
/// </summary>
public static partial class Leftovers
{
    static readonly string Pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    static readonly string Pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    static readonly string Win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static readonly string[] FolderBases =
    {
        Pf, Pf86,
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Local,
        System.IO.Path.Combine(Local, "Programs"),
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Local)!, "LocalLow"),
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
    };

    static readonly string[] RegBases =
    {
        @"HKEY_CURRENT_USER\Software",
        @"HKEY_LOCAL_MACHINE\SOFTWARE",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node",
    };

    /// <summary>Vendor names whose top-level folder/key is shared by many products; never offered as a whole.</summary>
    static readonly HashSet<string> SharedVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        "microsoft", "windows", "microsoftcorporation", "intel", "intelcorporation", "nvidia", "nvidiacorporation",
        "amd", "advancedmicrodevices", "google", "googlellc", "apple", "appleinc", "adobe", "adobesystems", "realtek",
        "oracle", "mozilla", "classes", "policies", "wow6432node", "commonfiles", "packages", "temp", "programs",
    };

    public static List<Leftover> Find(InstalledApp app, IEnumerable<InstalledApp> others, CancellationToken ct = default)
    {
        var names = NameTokens(app.Name, app.Publisher);
        var pubs = PublisherTokens(app.Publisher);
        // Vendor folder/key is offered whole only if no other installed app comes from the same publisher (Valve, Learning Machines...).
        var vendorShared = others.Any(o => o != app && PublisherTokens(o.Publisher).Overlaps(pubs));
        var otherDirs = others.Where(o => o != app).Select(o => o.InstallDir).Where(d => d != null).Select(d => d!).ToList();
        var result = new List<Leftover>();

        var installDir = app.InstallDir;
        if (installDir != null && Directory.Exists(installDir) && !IsUsedByOthers(installDir, otherDirs))
            result.Add(new Leftover { Kind = "Папка", Path = installDir, Size = Sys.DirSize(installDir, ct) }); // i18n: keep

        foreach (var dir in FindFolders(FolderBases, names, pubs, vendorShared))
            if (!result.Any(r => IsSameOrInside(dir, r.Path)) && !IsUsedByOthers(dir, otherDirs))
            {
                result.RemoveAll(r => IsSameOrInside(r.Path, dir));
                result.Add(new Leftover { Kind = "Папка", Path = dir, Size = Sys.DirSize(dir, ct) }); // i18n: keep
            }

        ct.ThrowIfCancellationRequested();
        foreach (var key in FindKeys(names, pubs, vendorShared))
            result.Add(new Leftover { Kind = "Ключ реестра", Path = key }); // i18n: keep
        if (Reg.Exists(app.KeyPath))
            result.Add(new Leftover { Kind = "Ключ реестра", Path = app.KeyPath }); // i18n: keep
        return result;
    }

    /// <summary>Folders under each base named like the app, or Vendor\App. The vendor folder itself is returned
    /// only when everything in it belongs to the app.</summary>
    public static List<string> FindFolders(IEnumerable<string> bases, ICollection<string> names, ICollection<string> pubs, bool vendorShared = false)
    {
        var found = new List<string>();
        foreach (var b in bases.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var dir in SafeDirs(b))
            {
                var n = Norm(System.IO.Path.GetFileName(dir));
                if (names.Contains(n)) found.Add(dir);
                else if (pubs.Contains(n) && !vendorShared && !SharedVendors.Contains(n))
                {
                    var children = SafeEntries(dir);
                    var matching = SafeDirs(dir).Where(c => names.Contains(Norm(System.IO.Path.GetFileName(c)))).ToList();
                    if (matching.Count > 0 && matching.Count == children.Count) found.Add(dir);
                    else found.AddRange(matching);
                }
                else if (pubs.Contains(n))
                    found.AddRange(SafeDirs(dir).Where(c => names.Contains(Norm(System.IO.Path.GetFileName(c)))));
            }
        }
        return found;
    }

    static List<string> FindKeys(ICollection<string> names, ICollection<string> pubs, bool vendorShared = false)
    {
        var found = new List<string>();
        foreach (var b in RegBases)
        {
            foreach (var sub in Reg.SubKeys(b))
            {
                var n = Norm(sub);
                var path = b + "\\" + sub;
                if (names.Contains(n) && !SharedVendors.Contains(n)) found.Add(path);
                else if (pubs.Contains(n))
                {
                    var children = Reg.SubKeys(path);
                    var matching = children.Where(c => names.Contains(Norm(c))).Select(c => path + "\\" + c).ToList();
                    using var k = Reg.Open(path);
                    var hasValues = k != null && Reg.ValueNames(k).Length > 0;
                    if (!vendorShared && !SharedVendors.Contains(n) && matching.Count > 0 && matching.Count == children.Length && !hasValues) found.Add(path);
                    else found.AddRange(matching);
                }
            }
        }
        return found;
    }

    // (anything in brackets) | version numbers | bitness
    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]|\b(v|version\s*)?\d+(\.\d+)+\S*|\b(x64|x86|64-bit|32-bit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRx();

    [GeneratedRegex(@"[,\.]?\s+(inc|llc|ltd|limited|gmbh|corp|corporation|co|s\.r\.o|ag|sa|bv|pty|software|technologies|games|studio|studios)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex CompanySuffixRx();

    public static string Norm(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>"Mozilla Firefox 128.0 (x64 ru)" → {"mozillafirefox", "firefox"}.</summary>
    public static HashSet<string> NameTokens(string name, string publisher)
    {
        var clean = VersionRx().Replace(name, "").Trim();
        var set = new HashSet<string> { Norm(clean) };
        foreach (var pub in PublisherTokens(publisher))
        {
            var words = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 1 && Norm(words[0]) == pub) set.Add(Norm(string.Join(' ', words.Skip(1))));
        }
        set.RemoveWhere(t => t.Length < 3);
        return set;
    }

    public static HashSet<string> PublisherTokens(string publisher)
    {
        var set = new HashSet<string>();
        var p = publisher.Trim();
        for (var i = 0; i < 3 && p != ""; i++)
        {
            set.Add(Norm(p));
            var stripped = CompanySuffixRx().Replace(p, "").Trim();
            if (stripped == p) break;
            p = stripped;
        }
        set.RemoveWhere(t => t.Length < 3);
        return set;
    }

    public static bool IsProtectedDir(string d)
    {
        d = d.TrimEnd('\\');
        if (d.Length <= 3) return true;
        string[] exact = { Pf, Pf86, Win, Local, System.IO.Path.Combine(Pf, "Common Files"), System.IO.Path.Combine(Pf86, "Common Files"),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.Desktop) };
        return exact.Any(e => e.Equals(d, StringComparison.OrdinalIgnoreCase)) || IsSameOrInside(d, Win);
    }

    /// <summary>Another installed app lives in it, or it lives inside another app (shared vendor dirs like AMD\CIM).</summary>
    static bool IsUsedByOthers(string dir, List<string> otherDirs) => otherDirs.Any(o => IsSameOrInside(o, dir) || IsSameOrInside(dir, o));

    public static bool IsSameOrInside(string path, string parent) =>
        path.TrimEnd('\\').Equals(parent.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    static List<string> SafeDirs(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir).ToList() : new(); } catch { return new(); }
    }

    static List<string> SafeEntries(string dir)
    {
        try { return Directory.GetFileSystemEntries(dir).ToList(); } catch { return new(); }
    }

    /// <summary>Deletes checked leftovers: folders to the Recycle Bin, keys through undo.</summary>
    public static (int Done, List<string> Failed) Remove(IEnumerable<Leftover> items, string title)
    {
        var failed = new List<string>();
        var done = 0;
        using var tx = UndoTx.Begin(title);
        foreach (var l in items.Where(i => i.Checked))
        {
            if (l.IsRegistry) tx.DeleteKey(l.Path);
            else if (Sys.Recycle(l.Path)) tx.Note(L.T("В корзину: ") + l.Path);
            else { failed.Add(l.Path); continue; }
            done++;
        }
        failed.AddRange(tx.Errors);
        return (done, failed);
    }
}

using RegKeeper.Core;

namespace RegKeeper.Modules;

public sealed class RegIssue
{
    public bool Checked { get; set; } = true;
    public string Category { get; init; } = "";
    public string Key { get; init; } = "";
    /// <summary>Null = the whole key is the problem.</summary>
    public string? ValueName { get; init; }
    public string Problem { get; init; } = "";
    public string Display => ValueName == null ? Reg.Short(Key) : $"{Reg.Short(Key)} → {(ValueName == "" ? "(По умолчанию)" : ValueName)}";
    public string Id => ValueName == null ? Key : Key + "|" + ValueName;
}

/// <summary>
/// Registry references to files that no longer exist. Only rooted paths on present drives count as missing
/// (Sys.IsMissing), so relative names, network paths and unplugged drives are never reported.
/// </summary>
public static class RegScanner
{
    const string Hklm = @"HKEY_LOCAL_MACHINE\", Hkcu = @"HKEY_CURRENT_USER\";
    const string Cv = @"SOFTWARE\Microsoft\Windows\CurrentVersion";
    const string Wow = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion";
    public static readonly string IgnorePath = Path.Combine(AppPaths.Data, "regignore.txt");

    public static List<RegIssue> Scan()
    {
        var ignore = LoadIgnore();
        var checks = new Func<IEnumerable<RegIssue>>[]
        {
            Uninstall, AppPathKeys, SharedDlls, MuiCache, Run, InstallerFolders, Applications, Firewall,
            () => Com(Hklm + @"SOFTWARE\Classes\CLSID"), () => Com(Hklm + @"SOFTWARE\WOW6432Node\Classes\CLSID"),
            () => Com(Hkcu + @"Software\Classes\CLSID"), TypeLibs,
        };
        return checks.AsParallel().SelectMany(c => c().ToList()).Where(i => !ignore.Contains(i.Id))
            .OrderBy(i => i.Category).ThenBy(i => i.Key).ToList();
    }

    static IEnumerable<RegIssue> Uninstall()
    {
        foreach (var root in new[] { Hklm + Cv, Hklm + Wow, Hkcu + Cv })
        {
            var baseKey = root + @"\Uninstall";
            foreach (var sub in Reg.SubKeys(baseKey))
            {
                var key = baseKey + "\\" + sub;
                var cmd = Reg.GetString(key, "UninstallString");
                if (cmd == null || cmd.Contains("msiexec", StringComparison.OrdinalIgnoreCase) || !Sys.IsMissing(cmd)) continue;
                var loc = Reg.GetString(key, "InstallLocation");
                if (!string.IsNullOrWhiteSpace(loc) && !Sys.IsMissing(loc)) continue; // app still on disk, only its uninstaller is gone
                yield return new RegIssue { Category = "Деинсталляция", Key = key, Problem = "Нет деинсталлятора: " + Sys.ExtractPath(cmd) };
            }
        }
    }

    static IEnumerable<RegIssue> AppPathKeys()
    {
        foreach (var root in new[] { Hklm + Cv, Hklm + Wow, Hkcu + Cv })
        {
            var baseKey = root + @"\App Paths";
            foreach (var sub in Reg.SubKeys(baseKey))
                if (Reg.GetString(baseKey + "\\" + sub, "") is { } path && Sys.IsMissing(path))
                    yield return new RegIssue { Category = "Пути приложений", Key = baseKey + "\\" + sub, Problem = "Нет файла: " + Sys.ExtractPath(path) };
        }
    }

    static IEnumerable<RegIssue> ValuesNamedByPath(string key, string category, Func<string, string>? toPath = null)
    {
        using var k = Reg.Open(key);
        if (k == null) yield break;
        foreach (var name in Reg.ValueNames(k))
        {
            var path = toPath?.Invoke(name) ?? name;
            if (path != "" && Sys.IsMissing(path))
                yield return new RegIssue { Category = category, Key = key, ValueName = name, Problem = "Нет файла: " + path };
        }
    }

    static IEnumerable<RegIssue> SharedDlls() =>
        ValuesNamedByPath(Hklm + Cv + @"\SharedDLLs", "Общие библиотеки")
            .Concat(ValuesNamedByPath(Hklm + Wow + @"\SharedDLLs", "Общие библиотеки"));

    static IEnumerable<RegIssue> InstallerFolders() =>
        ValuesNamedByPath(Hklm + Cv + @"\Installer\Folders", "Папки установщика");

    /// <summary>MUICache value names are "C:\path\app.exe.FriendlyAppName" / ".ApplicationCompany".</summary>
    static IEnumerable<RegIssue> MuiCache() =>
        ValuesNamedByPath(Hkcu + @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", "Кэш имён программ",
            n => n.StartsWith("LangID") ? "" : n.EndsWith(".FriendlyAppName") || n.EndsWith(".ApplicationCompany") ? n[..n.LastIndexOf('.')] : n);

    static IEnumerable<RegIssue> Run() =>
        Autoruns.RunAndFolders().Where(i => i.Kind == StartupKind.Registry && Sys.IsMissing(i.Command))
            .Select(i => new RegIssue { Category = "Автозагрузка", Key = i.Location, ValueName = i.Name, Problem = "Нет файла: " + Sys.ExtractPath(i.Command) });

    static IEnumerable<RegIssue> Applications()
    {
        var baseKey = Hkcu + @"Software\Classes\Applications";
        foreach (var app in Reg.SubKeys(baseKey))
            if (Reg.GetString($@"{baseKey}\{app}\shell\open\command", "") is { } cmd && Sys.IsMissing(cmd))
                yield return new RegIssue { Category = "Открыть с помощью", Key = baseKey + "\\" + app, Problem = "Нет файла: " + Sys.ExtractPath(cmd) };
    }

    /// <summary>Rule data is "v2.31|Action=Allow|...|App=C:\x.exe|...".</summary>
    static IEnumerable<RegIssue> Firewall()
    {
        var key = Hklm + @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";
        using var k = Reg.Open(key);
        if (k == null) yield break;
        foreach (var name in Reg.ValueNames(k))
        {
            var app = Reg.GetString(k, name)?.Split('|').FirstOrDefault(p => p.StartsWith("App=", StringComparison.OrdinalIgnoreCase))?[4..];
            if (name.StartsWith('{') && app != null && Sys.IsMissing(app)) // non-GUID names are built-in Windows rules
                yield return new RegIssue { Category = "Правила брандмауэра", Key = key, ValueName = name, Problem = "Нет программы: " + app };
        }
    }

    /// <summary>A 32-bit process expands these to the x86 folders. Windows itself leaves WOW entries whose
    /// x86 file is absent but the x64 one exists (ink\tipskin.dll), so a WOW entry is broken only if both are gone.</summary>
    static string As32(string s) => s.Replace("%ProgramFiles%", "%ProgramFiles(x86)%", StringComparison.OrdinalIgnoreCase)
        .Replace("%CommonProgramFiles%", "%CommonProgramFiles(x86)%", StringComparison.OrdinalIgnoreCase);

    static IEnumerable<RegIssue> Com(string clsidRoot)
    {
        var wow = clsidRoot.Contains("WOW6432Node");
        foreach (var clsid in Reg.SubKeys(clsidRoot))
            foreach (var server in new[] { "InprocServer32", "LocalServer32" })
                if (Reg.GetString($@"{clsidRoot}\{clsid}\{server}", "") is { } path && MissingNonSystem(path) && !(wow && !MissingNonSystem(As32(path))))
                {
                    yield return new RegIssue { Category = "COM-объекты", Key = clsidRoot + "\\" + clsid, Problem = $"{server}: нет файла {Sys.ExtractPath(path)}" };
                    break;
                }
    }

    /// <summary>TypeLib\{guid}\{version}\{lcid}\win32|win64 → file. A version is broken when none of its files exist.</summary>
    static IEnumerable<RegIssue> TypeLibs()
    {
        var root = Hklm + @"SOFTWARE\Classes\TypeLib"; // shared between views, WOW6432Node\Classes\TypeLib is the same key
        foreach (var guid in Reg.SubKeys(root))
                foreach (var ver in Reg.SubKeys($@"{root}\{guid}"))
                {
                    var verKey = $@"{root}\{guid}\{ver}";
                    var files = Reg.SubKeys(verKey).Where(l => l.All(char.IsAsciiHexDigit))
                        .SelectMany(l => Reg.SubKeys($@"{verKey}\{l}").Select(p => Reg.GetString($@"{verKey}\{l}\{p}", "")))
                        .Where(p => !string.IsNullOrEmpty(p)).ToList();
                    if (files.Count > 0 && files.All(MissingNonSystem))
                        yield return new RegIssue { Category = "Библиотеки типов", Key = verKey, Problem = "Нет файла: " + Sys.ExtractPath(files[0]) };
                }
    }

    static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";

    /// <summary>Windows ships its own dangling COM/TypeLib registrations (WOW64 views, optional features); not ours to delete.</summary>
    static bool MissingNonSystem(string path) =>
        Sys.IsMissing(path) && Sys.ExtractPath(path)?.StartsWith(WinDir, StringComparison.OrdinalIgnoreCase) == false;

    public static HashSet<string> LoadIgnore() =>
        File.Exists(IgnorePath) ? File.ReadAllLines(IgnorePath).Where(l => l != "").ToHashSet(StringComparer.OrdinalIgnoreCase) : new(StringComparer.OrdinalIgnoreCase);

    public static void AddIgnore(IEnumerable<RegIssue> issues) => File.AppendAllLines(IgnorePath, issues.Select(i => i.Id));

    public static List<string> Fix(IEnumerable<RegIssue> issues)
    {
        using var tx = UndoTx.Begin("Чистка реестра");
        foreach (var i in issues)
            if (i.ValueName == null) tx.DeleteKey(i.Key);
            else tx.DeleteValue(i.Key, i.ValueName);
        return tx.Errors;
    }
}

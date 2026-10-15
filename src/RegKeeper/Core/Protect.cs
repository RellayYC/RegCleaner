namespace RegKeeper.Core;

/// <summary>
/// Last line of defence for every delete in the app (UndoTx, Sys.Recycle, disk cleanup): system keys and folders,
/// their ancestors, and everything inside the critical ones are refused no matter what a scan or the user selected.
/// </summary>
public static class Protect
{
    const string Lm = @"HKEY_LOCAL_MACHINE\", Cu = @"HKEY_CURRENT_USER\", Cv = @"Microsoft\Windows\CurrentVersion";

    /// <summary>These keys and their ancestors; subkeys stay deletable (a program's own Run value, CLSID, service...).</summary>
    static readonly string[] Keys =
    {
        Lm + @"SOFTWARE\" + Cv + @"\Run", Lm + @"SOFTWARE\" + Cv + @"\RunOnce", Lm + @"SOFTWARE\" + Cv + @"\Uninstall",
        Lm + @"SOFTWARE\" + Cv + @"\App Paths", Lm + @"SOFTWARE\" + Cv + @"\Explorer\StartupApproved", Lm + @"SOFTWARE\" + Cv + @"\Policies",
        Lm + @"SOFTWARE\" + Cv + @"\SharedDLLs", Lm + @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts",
        Lm + @"SOFTWARE\WOW6432Node\" + Cv + @"\Run", Lm + @"SOFTWARE\WOW6432Node\" + Cv + @"\Uninstall",
        Lm + @"SOFTWARE\Classes\CLSID", Lm + @"SOFTWARE\Classes\TypeLib", Lm + @"SOFTWARE\Classes\Interface", Lm + @"SOFTWARE\Policies",
        Lm + @"SYSTEM\CurrentControlSet\Services",
        Cu + @"Software\" + Cv + @"\Run", Cu + @"Software\" + Cv + @"\RunOnce", Cu + @"Software\" + Cv + @"\Uninstall",
        Cu + @"Software\" + Cv + @"\Explorer\StartupApproved", Cu + @"Software\" + Cv + @"\Explorer\FileExts", Cu + @"Software\" + Cv + @"\Policies",
        Cu + @"Software\Classes\CLSID", Cu + @"Software\Classes\Local Settings", Cu + @"Software\Policies",
        Cu + @"Control Panel", Cu + "Environment", Cu + "Keyboard Layout", Cu + "Console",
        @"HKEY_CLASSES_ROOT\CLSID", @"HKEY_USERS\.DEFAULT", @"HKEY_USERS\S-1-5-18",
    };

    /// <summary>Nothing at or under these may be deleted.</summary>
    static readonly string[] SealedKeys =
    {
        Lm + "SAM", Lm + "SECURITY", Lm + "BCD00000000", Lm + "HARDWARE", Lm + "COMPONENTS", Lm + @"SYSTEM\Setup",
        Lm + @"SYSTEM\CurrentControlSet\Control", Lm + @"SYSTEM\CurrentControlSet\Enum",
        Lm + @"SYSTEM\ControlSet001\Control", Lm + @"SYSTEM\ControlSet001\Enum",
        Lm + @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", Lm + @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList",
        Lm + @"SOFTWARE\Microsoft\Cryptography", Lm + @"SOFTWARE\Microsoft\Windows Defender",
    };

    public static bool IsProtectedKey(string path)
    {
        path = Reg.Normalize(path);
        return !path.Contains('\\')
               || Keys.Any(k => Same(k, path) || Inside(k, path))
               || SealedKeys.Any(k => Same(k, path) || Inside(k, path) || Inside(path, k));
    }

    /// <summary>Values inside sealed keys (BootExecute, Winlogon Shell...) are protected too.</summary>
    public static bool IsProtectedValue(string key) => IsSealedKey(Reg.Normalize(key));

    static bool IsSealedKey(string path) => SealedKeys.Any(k => Same(k, path) || Inside(path, k));

    static readonly Lazy<string[]> Folders = new(() =>
    {
        var list = Enum.GetValues<Environment.SpecialFolder>().Select(Environment.GetFolderPath).Where(p => p != "").ToList();
        list.Add(Path.GetTempPath());
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        list.Add(Path.Combine(windows, "Temp"));
        var users = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (users != null && Directory.Exists(users)) list.AddRange(Directory.GetDirectories(users));
        return list.Select(Full).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    });

    static readonly Lazy<string[]> SealedFolders = new(() =>
    {
        var w = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return new[] { @"System32\drivers", @"System32\config", @"System32\DriverStore", "SysWOW64", "WinSxS", "Boot", "servicing", "assembly", @"Microsoft.NET" }
            .Select(s => Full(Path.Combine(w, s))).ToArray();
    });

    static readonly string[] SystemBinaries = { ".exe", ".dll", ".sys", ".mui", ".efi", ".ocx", ".cpl", ".drv" };

    public static bool IsProtectedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        path = Full(path);
        if (Path.GetPathRoot(path) is { } root && Same(Full(root), path)) return true;
        if (Folders.Value.Any(f => Same(f, path) || Inside(f, path))) return true;
        if (SealedFolders.Value.Any(f => Same(f, path) || Inside(path, f))) return true;
        var windows = Full(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        return Inside(path, windows) && SystemBinaries.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    static string Full(string p) => Path.GetFullPath(p).TrimEnd('\\');
    static bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
    /// <summary>Is <paramref name="inner"/> strictly inside <paramref name="outer"/>.</summary>
    static bool Inside(string inner, string outer) => inner.StartsWith(outer + "\\", StringComparison.OrdinalIgnoreCase);
}

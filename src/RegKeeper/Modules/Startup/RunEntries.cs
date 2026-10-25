using RegKeeper.Core;

namespace RegKeeper.Modules;

// No WPF in this file: the watcher compiles it on its own (with Core\Reg, Sys, Protect and L), the app adds Autoruns.cs.

public enum StartupKind { Registry, Folder, Task, Service }

public sealed partial class StartupItem
{
    public StartupKind Kind { get; init; }
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    /// <summary>Run key, startup file, task path or service key.</summary>
    public string Location { get; init; } = "";
    /// <summary>StartupApproved key holding the on/off flag; null for RunOnce (can't be disabled, only deleted).</summary>
    public string? Approved { get; init; }
    public bool Enabled { get; set; }
}

public static partial class Autoruns
{
    const string Cv = @"SOFTWARE\Microsoft\Windows\CurrentVersion";
    const string Hkcu = @"HKEY_CURRENT_USER\", Hklm = @"HKEY_LOCAL_MACHINE\";
    const string Approved = @"\Explorer\StartupApproved\";

    public static readonly (string Key, string? Approved)[] RunKeys =
    {
        (Hkcu + Cv + @"\Run", Hkcu + Cv + Approved + "Run"),
        (Hkcu + Cv + @"\RunOnce", null),
        (Hklm + Cv + @"\Run", Hklm + Cv + Approved + "Run"),
        (Hklm + Cv + @"\RunOnce", null),
        (Hklm + @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", Hklm + Cv + Approved + "Run32"),
        (Hklm + @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce", null),
    };

    public static readonly (string Dir, string Approved)[] Folders =
    {
        (Environment.GetFolderPath(Environment.SpecialFolder.Startup), Hkcu + Cv + Approved + "StartupFolder"),
        (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Hklm + Cv + Approved + "StartupFolder"),
    };

    public static IEnumerable<StartupItem> RunAndFolders()
    {
        foreach (var (key, approved) in RunKeys)
        {
            using var k = Reg.Open(key);
            if (k == null) continue;
            foreach (var name in Reg.ValueNames(k))
                if (name != "" && Reg.GetString(k, name) is { } cmd)
                    yield return new StartupItem
                    {
                        Kind = StartupKind.Registry, Name = name, Command = cmd, Location = key, Approved = approved,
                        Enabled = approved == null || IsOn(approved, name)
                    };
        }

        foreach (var (dir, approved) in Folders)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir).Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                yield return new StartupItem
                {
                    Kind = StartupKind.Folder, Name = Path.GetFileNameWithoutExtension(file), Location = file, Approved = approved,
                    Command = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? Sys.ShortcutTarget(file) ?? file : file,
                    Enabled = IsOn(approved, Path.GetFileName(file))
                };
        }
    }

    /// <summary>StartupApproved: no value = on; byte 0 odd = off (02 on, 03 off, + FILETIME of the switch).</summary>
    static bool IsOn(string approved, string name) => Reg.Read(approved, name) is not { Data.Length: > 0 } v || (v.Data[0] & 1) == 0;
}

using System.Diagnostics;
using RegKeeper.Core;

namespace RegKeeper.Modules;

/// <summary>Per-user hooks into Windows: "Uninstall with RegKeeper" verb on exe/lnk and the watcher autostart.</summary>
public static class Integration
{
    const string Verb = "RegKeeperUninstall";
    static readonly string[] VerbKeys = { @"HKEY_CURRENT_USER\Software\Classes\exefile\shell\" + Verb, @"HKEY_CURRENT_USER\Software\Classes\lnkfile\shell\" + Verb };
    const string RunKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run", RunName = "RegKeeper Watcher";

    public static string AppExe => Path.Combine(AppContext.BaseDirectory, "RegKeeper.exe");
    public static string WatcherExe => Path.Combine(AppContext.BaseDirectory, "RegKeeper.Watcher.exe");

    public static bool ShellVerb => VerbKeys.All(Reg.Exists);

    public static void SetShellVerb(bool on)
    {
        foreach (var key in VerbKeys)
        {
            if (!on) { Reg.DeleteTree(key); continue; }
            using (var k = Reg.Create(key))
            {
                Reg.Write(k, RawValue.String("", L.T("Удалить с RegKeeper")));
                Reg.Write(k, RawValue.String("Icon", AppExe));
            }
            using var c = Reg.Create(key + @"\command");
            Reg.Write(c, RawValue.String("", $"\"{AppExe}\" --uninstall \"%1\""));
        }
    }

    public static bool WatcherAutostart => Reg.GetString(RunKey, RunName) != null;

    public static void SetWatcherAutostart(bool on)
    {
        using var k = Reg.Create(RunKey);
        if (on) Reg.Write(k, RawValue.String(RunName, $"\"{WatcherExe}\""));
        else k.DeleteValue(RunName, false);
    }

    public static void StartWatcher()
    {
        if (File.Exists(WatcherExe) && Process.GetProcessesByName("RegKeeper.Watcher").Length == 0)
            Process.Start(new ProcessStartInfo(WatcherExe) { UseShellExecute = true });
    }
}

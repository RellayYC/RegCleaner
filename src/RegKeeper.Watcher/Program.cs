using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using RegKeeper.Modules;

namespace RegKeeper.Watcher;

/// <summary>Tray app: notices new Run-key / Startup-folder entries and offers to review them in RegKeeper.</summary>
static class Program
{
    static readonly string StateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RegKeeper", "watcher-known.txt");

    [STAThread]
    static void Main()
    {
        using var single = new Mutex(true, @"Local\RegKeeper.Watcher", out var first);
        if (!first) return;
        ApplicationConfiguration.Initialize();

        var fresh = !File.Exists(StateFile);
        var known = fresh ? StartupWatch.Ids(Autoruns.RunAndFolders())
            : new HashSet<string>(File.ReadAllLines(StateFile), StringComparer.OrdinalIgnoreCase);
        var unsaved = fresh && !Save(known);

        using var tray = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Shield,
            Text = L.T("RegKeeper: контроль автозагрузки"),
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        tray.ContextMenuStrip.Items.Add(L.T("Открыть автозагрузку"), null, (_, _) => OpenStartup());
        tray.ContextMenuStrip.Items.Add(L.T("Выход"), null, (_, _) => Application.Exit());
        tray.DoubleClick += (_, _) => OpenStartup();
        tray.BalloonTipClicked += (_, _) => OpenStartup();

        // ponytail: polls 6 keys + 2 folders every 5 s (negligible cost); RegNotifyChangeKeyValue if it ever matters
        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += (_, _) =>
        {
            List<StartupItem> current;
            try { current = Autoruns.RunAndFolders().ToList(); }
            catch { return; }
            var added = StartupWatch.Added(current, known);
            var ids = StartupWatch.Ids(current);
            if (unsaved || !ids.SetEquals(known)) unsaved = !Save(ids); // written on a change (or after a failed write), not every tick
            known = ids;
            if (added.Count == 0) return;
            tray.ShowBalloonTip(10000, L.T("Новая программа в автозагрузке"),
                string.Join("\n", added.Select(a => $"{a.Name}: {a.Command}")) + L.T("\nНажмите, чтобы проверить."), ToolTipIcon.Warning);
        };
        timer.Start();
        Application.Run();
        tray.Visible = false;
    }

    static bool Save(HashSet<string> ids)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllLines(StateFile, ids);
            return true;
        }
        catch { return false; } // next tick retries
    }

    static void OpenStartup()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "RegKeeper.exe");
        try { Process.Start(new ProcessStartInfo(exe, "--startup") { UseShellExecute = true }); }
        catch { /* UAC declined */ }
    }
}

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

        var known = File.Exists(StateFile)
            ? new HashSet<string>(File.ReadAllLines(StateFile), StringComparer.OrdinalIgnoreCase)
            : Save(StartupWatch.Ids(Autoruns.RunAndFolders()));

        using var tray = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Shield,
            Text = "RegKeeper: контроль автозагрузки",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        tray.ContextMenuStrip.Items.Add("Открыть автозагрузку", null, (_, _) => OpenStartup());
        tray.ContextMenuStrip.Items.Add("Выход", null, (_, _) => Application.Exit());
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
            known = StartupWatch.Ids(current);
            Save(known);
            if (added.Count == 0) return;
            tray.ShowBalloonTip(10000, "Новая программа в автозагрузке",
                string.Join("\n", added.Select(a => $"{a.Name}: {a.Command}")) + "\nНажмите, чтобы проверить.", ToolTipIcon.Warning);
        };
        timer.Start();
        Application.Run();
        tray.Visible = false;
    }

    static HashSet<string> Save(HashSet<string> ids)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllLines(StateFile, ids);
        }
        catch { /* next tick retries */ }
        return ids;
    }

    static void OpenStartup()
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "RegKeeper.exe");
        try { Process.Start(new ProcessStartInfo(exe, "--startup") { UseShellExecute = true }); }
        catch { /* UAC declined */ }
    }
}

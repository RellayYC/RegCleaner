using System.ServiceProcess;
using System.Windows.Media;
using RegKeeper.Core;
using TS = Microsoft.Win32.TaskScheduler;

namespace RegKeeper.Modules;

public sealed partial class StartupItem
{
    public string Publisher { get; set; } = "";
    public bool Missing { get; set; }
    public ImageSource? Icon { get; set; }
    /// <summary>Logon-to-ready time Windows measured for this program (Diagnostics-Performance event 101); null when never measured.</summary>
    public double? Seconds { get; set; }
    public bool Measured => Seconds != null;
    public string SecondsText => Seconds is { } s ? L.T($"{s:0.#} с") : "";
    // the bar's share of the 0-40 s axis
    public System.Windows.GridLength BarLength => new(Math.Min(Seconds ?? 0, 40), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength BarRest => new(40 - Math.Min(Seconds ?? 0, 40), System.Windows.GridUnitType.Star);

    public bool CanToggle => Kind is StartupKind.Task or StartupKind.Service || Approved != null;
    public string ApprovedName => Kind == StartupKind.Folder ? Path.GetFileName(Location) : Name;
    public string Status => Missing ? L.T("файл не найден") : "";
    public string KindText => Kind switch
    {
        StartupKind.Registry => Approved == null ? L.T("Реестр (однократно)") : L.T("Реестр"),
        StartupKind.Folder => L.T("Папка автозагрузки"),
        StartupKind.Task => L.T("Планировщик"),
        _ => L.T("Служба")
    };
}

/// <summary>
/// Autostart entries. On/off for Run keys and startup folders uses Windows' own Explorer\StartupApproved flags
/// (the same switch Task Manager flips), so the entry itself never moves.
/// </summary>
public static partial class Autoruns
{
    const string Services = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services";
    /// <summary>Services we switched to manual; remembered so they stay in the list.</summary>
    const string DisabledServices = @"HKEY_LOCAL_MACHINE\SOFTWARE\RegKeeper\DisabledServices";

    public static List<StartupItem> Load()
    {
        var items = RunAndFolders().Concat(Tasks()).Concat(ServiceItems()).ToList();
        foreach (var i in items)
        {
            i.Publisher = Sys.Company(Sys.ExtractPath(i.Command));
            i.Missing = Sys.IsMissing(i.Command);
            i.Icon = Inventory.LoadIcon(Sys.ExtractPath(i.Command));
        }
        return items;
    }

    static IEnumerable<StartupItem> Tasks()
    {
        var tasks = TS.TaskService.Instance.FindAllTasks(t =>
        {
            try
            {
                return !t.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
                       && t.Definition.Triggers.Any(tr => tr is TS.LogonTrigger or TS.BootTrigger);
            }
            catch { return false; }
        });
        foreach (var t in tasks)
            yield return new StartupItem
            {
                Kind = StartupKind.Task, Name = t.Name, Location = t.Path, Enabled = t.Enabled,
                Command = string.Join(" & ", t.Definition.Actions.OfType<TS.ExecAction>()
                    .Select(a => a.Path.Trim('"') is var p && p.Contains(' ') ? $"\"{p}\" {a.Arguments}".Trim() : $"{p} {a.Arguments}".Trim()))
            };
    }

    /// <summary>Third-party auto-start services (plus the ones we put on manual).</summary>
    static IEnumerable<StartupItem> ServiceItems()
    {
        HashSet<string> mine;
        using (var k = Reg.Open(DisabledServices))
            mine = new(k == null ? Array.Empty<string>() : Reg.ValueNames(k), StringComparer.OrdinalIgnoreCase);

        foreach (var sc in ServiceController.GetServices())
        {
            using (sc)
            {
                var key = Services + "\\" + sc.ServiceName;
                using var k = Reg.Open(key);
                if (k?.GetValue("Start") is not int start) continue;
                if (start != 2 && !mine.Contains(sc.ServiceName)) continue;
                var image = Reg.GetString(k, "ImagePath") ?? "";
                if (Sys.IsMicrosoft(Sys.ExtractPath(image))) continue;
                yield return new StartupItem { Kind = StartupKind.Service, Name = sc.DisplayName, Command = image, Location = key, Enabled = start == 2 };
            }
        }
    }

    public static void SetEnabled(StartupItem item, bool on, UndoTx tx)
    {
        switch (item.Kind)
        {
            case StartupKind.Registry or StartupKind.Folder when item.Approved != null:
                var flag = new byte[12];
                flag[0] = (byte)(on ? 2 : 3);
                if (!on) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(flag, 4);
                tx.SetValue(item.Approved, new RawValue(item.ApprovedName, RawValue.Binary, flag));
                break;
            case StartupKind.Task:
                using (var t = TS.TaskService.Instance.GetTask(item.Location) ?? throw new IOException(L.T("Задача не найдена: ") + item.Location))
                {
                    tx.BackupTask(t.Path, t.Xml);
                    t.Enabled = on;
                }
                break;
            case StartupKind.Service:
                var svc = Reg.Leaf(item.Location);
                if (on) tx.DeleteValue(DisabledServices, svc);
                else tx.SetValue(DisabledServices, RawValue.String(svc, ""));
                tx.SetValue(item.Location, RawValue.FromDword("Start", on ? 2u : 3u));
                break;
            default:
                throw new InvalidOperationException(L.T("Однократный запуск (RunOnce) нельзя отключить, только удалить."));
        }
        item.Enabled = on;
    }

    public static void Delete(StartupItem item, UndoTx tx)
    {
        switch (item.Kind)
        {
            case StartupKind.Registry:
                tx.DeleteValue(item.Location, item.Name);
                break;
            case StartupKind.Folder:
                if (!Sys.Recycle(item.Location)) throw new IOException(L.T("Не удалось удалить файл: ") + item.Location);
                tx.Note(L.T("В корзину: ") + item.Location);
                break;
            case StartupKind.Task:
                using (var t = TS.TaskService.Instance.GetTask(item.Location) ?? throw new IOException(L.T("Задача не найдена: ") + item.Location))
                {
                    tx.BackupTask(t.Path, t.Xml);
                    t.Folder.DeleteTask(t.Name, false);
                }
                return;
            default:
                throw new InvalidOperationException(L.T("Службы не удаляются, только переводятся на ручной запуск."));
        }
        if (item.Approved != null) tx.DeleteValue(item.Approved, item.ApprovedName);
    }

    /// <summary>
    /// Delayed start: the Run/folder entry is switched off and a logon task with a delay runs the same thing. The task
    /// is an ordinary entry in this list (toggle, delete), and undo removes it and switches the entry back on.
    /// </summary>
    // ponytail: the task fires for the current user only, even for an all-users entry; per-user tasks if anyone asks
    public static string Delay(StartupItem item, TimeSpan delay, UndoTx tx)
    {
        if (item.Kind is not (StartupKind.Registry or StartupKind.Folder) || item.Approved == null)
            throw new InvalidOperationException(L.T("Отложить можно только запись реестра Run или файл из папки автозагрузки."));
        var (exe, args) = item.Kind == StartupKind.Folder ? ("explorer.exe", $"\"{item.Location}\"") : SplitCommand(item.Command);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        var td = TS.TaskService.Instance.NewTask();
        td.RegistrationInfo.Description = L.T("RegKeeper: отложенный запуск ") + item.Name;
        td.Principal.UserId = user;
        td.Principal.LogonType = TS.TaskLogonType.InteractiveToken;
        td.Triggers.Add(new TS.LogonTrigger { UserId = user, Delay = delay });
        td.Actions.Add(new TS.ExecAction(exe, args == "" ? null : args));
        td.Settings.ExecutionTimeLimit = TimeSpan.Zero; // the default 72 h limit would kill a tray app
        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        var name = L.T("RegKeeper - отложенно - ") + string.Concat(item.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        SetEnabled(item, false, tx);
        TS.TaskService.Instance.RootFolder.RegisterTaskDefinition(name, td);
        tx.CreatedTask(@"\" + name);
        return @"\" + name;
    }

    /// <summary>Command line → program and its arguments; handles quotes and unquoted paths with spaces.</summary>
    public static (string Exe, string Args) SplitCommand(string cmd)
    {
        var s = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            return end < 0 ? (s[1..], "") : (s[1..end], s[(end + 1)..].Trim());
        }
        if (Sys.ExtractPath(s) is { } p && s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return (p, s[p.Length..].Trim());
        var cut = s.IndexOf(' '); // rundll32 and friends: ExtractPath names the dll, the program is the first word
        return cut < 0 ? (s, "") : (s[..cut], s[(cut + 1)..].Trim());
    }

    public static void Add(string name, string command, bool allUsers, UndoTx tx) =>
        tx.SetValue(RunKeys[allUsers ? 2 : 0].Key, RawValue.String(name, command));
}

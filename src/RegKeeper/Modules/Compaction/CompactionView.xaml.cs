using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class CompactionView : UserControl
{
    List<HiveInfo> _hives = new();

    public CompactionView()
    {
        InitializeComponent();
        L.Apply(this);
        try { _hives = Compaction.Plan(); }
        catch (Exception e) { Ui.Error(e); }
        Grid.ItemsSource = _hives;
        AnalyzeBtn.IsEnabled = Compaction.IsAdmin;
        Status.Text = Compaction.IsAdmin
            ? L.T($"Разделов: {_hives.Count}   Всего: {Sys.Size(_hives.Sum(h => h.Size))}")
            : L.T("Нужны права администратора: перезапустите RegKeeper от имени администратора.");
    }

    async void Analyze_Click(object s, RoutedEventArgs e)
    {
        try { await Ui.Busy(this, () => Task.Run(() => Compaction.Analyze(_hives))); }
        catch (Exception ex) { Ui.Error(ex); }
        Grid.Items.Refresh();
        var gain = _hives.Sum(h => h.Gain);
        Status.Text = L.T($"Можно освободить: {Sys.Size(gain)}");
        CompactBtn.IsEnabled = _hives.Any(h => h.Gain > 0);
    }

    async void Compact_Click(object s, RoutedEventArgs e)
    {
        var todo = _hives.Where(h => h.Gain > 0).ToList();
        if (todo.Count == 0) return;
        if (!Ui.Confirm(L.T($"Сжать разделов: {todo.Count}, выигрыш {Sys.Size(todo.Sum(h => h.Gain))}?\nЗакройте все программы. Замена файлов произойдёт при перезагрузке."))) return;
        if (!Ui.Confirm(L.T("Точно? Это меняет файлы реестра Windows. Перед заменой будет создана точка восстановления."), L.T("Подтверждение"))) return;

        var restorePoint = false;
        var errors = new List<string>();
        await Ui.Busy(this, () => Task.Run(() =>
        {
            restorePoint = Compaction.CreateRestorePoint(L.T("RegKeeper: сжатие реестра"));
            if (!restorePoint) return;
            foreach (var h in todo)
                try { Compaction.Replace(h, h.Compacted + ".old"); }
                catch (Exception ex) { errors.Add($"{h.Key}: {ex.Message}"); }
        }));
        if (!restorePoint)
        {
            Ui.Info(L.T("Не удалось создать точку восстановления (защита системы выключена?). Сжатие отменено."));
            return;
        }
        CompactBtn.IsEnabled = false;
        if (errors.Count > 0) Ui.Info(L.T("Не удалось:\n") + string.Join("\n", errors));
        if (errors.Count < todo.Count && Ui.Confirm(L.T("Сжатие применится после перезагрузки. Перезагрузить сейчас?")))
            Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true });
    }

    void Folder_Click(object s, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Compaction.Dir);
        Sys.Open(Compaction.Dir);
    }
}

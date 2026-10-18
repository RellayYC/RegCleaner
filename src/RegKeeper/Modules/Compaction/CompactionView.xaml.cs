using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

/// <summary>A hive row: the bar is as long as the file against the biggest hive; analysed, it splits into data and what compaction frees.</summary>
public sealed record HiveRow(HiveInfo H, long Max)
{
    static GridLength Star(long v) => new(v, GridUnitType.Star);
    public string Key => Reg.Short(H.Key);
    public string File => H.File;
    public GridLength Used => Star(H.Size);
    public GridLength Rest => Star(Max - H.Size);
    public GridLength Data => Star(H.Size - H.Gain);
    public GridLength Free => Star(H.Gain);
    public string Sizes => H.Compacted != null ? $"{H.SizeText} → {H.CompactedText}" : H.SizeText;
    public string Saving => H.Compacted != null ? "−" + Sys.Size(H.Gain) : "-";
}

public partial class CompactionView : UserControl
{
    List<HiveInfo> _hives = new();

    public CompactionView()
    {
        InitializeComponent();
        L.Apply(this);
        try { _hives = Compaction.Plan(); }
        catch (Exception e) { Ui.Error(e); }
        AnalyzeBtn.IsEnabled = Compaction.IsAdmin;
        Status.Visibility = Compaction.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
        Show();
    }

    void Show()
    {
        var max = _hives.Select(h => h.Size).DefaultIfEmpty(0).Max();
        Hives.ItemsSource = _hives.Select(h => new HiveRow(h, max)).ToList();
        long size = _hives.Sum(h => h.Size), gain = _hives.Sum(h => h.Gain);
        var analyzed = _hives.Any(h => h.Compacted != null);
        SizeText.Text = Sys.Size(size);
        AfterText.Text = analyzed ? Sys.Size(size - gain) : "-";
        GainPill.Visibility = analyzed ? Visibility.Visible : Visibility.Collapsed;
        GainText.Text = L.T($"Экономия {Sys.Size(gain)} · {100.0 * gain / Math.Max(1, size):0.#}%");
        CountText.Text = L.T($"Разделов: {_hives.Count}");
    }

    async void Analyze_Click(object s, RoutedEventArgs e)
    {
        try { await Ui.Busy(this, () => Task.Run(() => Compaction.Analyze(_hives))); }
        catch (Exception ex) { Ui.Error(ex); }
        Show();
        CompactBtn.IsEnabled = _hives.Any(h => h.Gain > 0);
        Steps(CompactBtn.IsEnabled ? 2 : 1);
    }

    async void Compact_Click(object s, RoutedEventArgs e)
    {
        var todo = _hives.Where(h => h.Gain > 0).ToList();
        if (todo.Count == 0) return;
        if (Dialog.Show(L.T("Сжать реестр?"), [L.T("Отмена"), L.T("Сжать")], 1, Sys.Size(todo.Sum(h => h.Gain)), L.T($"освободится в {todo.Count} разделах"),
                (todo.Sum(h => h.Size), todo.Sum(h => h.CompactedSize)), L.T("Закройте все программы. Файлы заменятся при перезагрузке.")) != 1) return;
        var withPoint = RestorePoint.IsChecked == true;
        if (!Ui.Confirm(withPoint
            ? L.T("Точно? Это меняет файлы реестра Windows. Перед заменой будет создана точка восстановления.")
            : L.T("Точно? Это меняет файлы реестра Windows. Точка восстановления создана не будет."), L.T("Подтверждение"))) return;

        var restorePoint = false;
        var errors = new List<string>();
        await Ui.Busy(this, () => Task.Run(() =>
        {
            if (withPoint)
            {
                restorePoint = Compaction.CreateRestorePoint(L.T("RegKeeper: сжатие реестра"));
                if (!restorePoint) return;
            }
            foreach (var h in todo)
                try { Compaction.Replace(h, h.Compacted + ".old"); }
                catch (Exception ex) { errors.Add($"{h.Key}: {ex.Message}"); }
        }));
        if (withPoint && !restorePoint)
        {
            Ui.Info(L.T("Не удалось создать точку восстановления (защита системы выключена?). Сжатие отменено."));
            return;
        }
        CompactBtn.IsEnabled = false;
        Steps(3);
        if (errors.Count > 0) Ui.Info(L.T("Не удалось:\n") + string.Join("\n", errors));
        if (errors.Count < todo.Count && Ui.Confirm(L.T("Сжатие применится после перезагрузки. Перезагрузить сейчас?")))
            Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true });
    }

    /// <summary>Marks the current step; the ones before it are done.</summary>
    void Steps(int current)
    {
        var dots = new[] { Step1, Step2, Step3 };
        for (var i = 0; i < 3; i++) dots[i].Tag = i + 1 == current ? "on" : i + 1 < current ? "done" : null;
    }

    void Folder_Click(object s, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Compaction.Dir);
        Sys.Open(Compaction.Dir);
    }
}

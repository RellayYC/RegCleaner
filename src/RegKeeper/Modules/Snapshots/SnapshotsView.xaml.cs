using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class SnapshotsView : UserControl
{
    List<SnapChange> _changes = new();
    string _source = "";
    DataGrid? _lastList;

    public SnapshotsView()
    {
        InitializeComponent();
        L.Apply(this);
        Roots.Text = string.Join("; ", Snapshot.RegRoots.Select(Reg.Short).Concat(Snapshot.FileRoots));
        Load();
    }

    static string Stamp() => DateTime.Now.ToString("yyyyMMdd_HHmmss");

    void Load()
    {
        static List<SnapInfo> Infos(string dir, string ext) => Directory.EnumerateFiles(dir, "*" + ext)
            .Select(f => { try { return Snapshot.Info(f); } catch { return null; } })
            .OfType<SnapInfo>().OrderByDescending(i => i.Date).ToList();
        SnapList.ItemsSource = Infos(AppPaths.Snapshots, ".rksnap");
        TraceList.ItemsSource = Infos(AppPaths.Traces, ".rktrace");
    }

    string[] RootList() => Roots.Text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    void Show(List<SnapChange> changes, string source)
    {
        _changes = changes;
        _source = source;
        ApplyFilter();
    }

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        var list = q == "" ? _changes : _changes.Where(c => c.Display.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        Changes.ItemsSource = list;
        Status.Text = _source == "" ? "" : L.T($"{_source}: изменений {_changes.Count}") + (list.Count != _changes.Count ? L.T($", показано {list.Count}") : "");
    }

    void Search_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();
    void List_GotFocus(object s, RoutedEventArgs e) => _lastList = (DataGrid)s;

    async void Take_Click(object s, RoutedEventArgs e)
    {
        if (Ui.Prompt(L.T("Снимок"), L.T("Название снимка:"), L.T("Снимок ") + DateTime.Now.ToString("dd.MM.yyyy HH:mm")) is not { } note) return;
        var roots = RootList();
        await Ui.Busy(this, ct => Task.Run(() => Snapshot.Take(Path.Combine(AppPaths.Snapshots, Stamp() + ".rksnap"), note, roots, ct)));
        Load();
    }

    async void Compare_Click(object s, RoutedEventArgs e)
    {
        var sel = SnapList.SelectedItems.Cast<SnapInfo>().OrderBy(i => i.Date).ToList();
        if (sel.Count != 2) { Ui.Info(L.T("Выберите два снимка (Ctrl+клик).")); return; }
        await Ui.Busy(this, async () =>
        {
            var changes = await Task.Run(() => Snapshot.Diff(Snapshot.Read(sel[0].File), Snapshot.Read(sel[1].File)).ToList());
            Show(changes, $"«{sel[0].Note}» → «{sel[1].Note}»");
        });
    }

    async void Trace_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = L.T("Установщики (*.exe;*.msi)|*.exe;*.msi|Все файлы|*.*") };
        if (dlg.ShowDialog() != true) return;
        if (Ui.Prompt(L.T("Отслеживание"), L.T("Название программы:"), Path.GetFileNameWithoutExtension(dlg.FileName)) is not { } note) return;
        var roots = RootList();
        string before = Path.Combine(AppPaths.Snapshots, "~before.tmp"), after = Path.Combine(AppPaths.Snapshots, "~after.tmp");
        try
        {
            var ok = false;
            await Ui.Busy(this, async ct =>
            {
                await Task.Run(() => Snapshot.Take(before, "before", roots, ct));
                Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                ok = true;
            });
            if (!ok || !Ui.Confirm(L.T("Установка запущена. Пройдите её до конца (и закройте программу, если она запустилась), затем нажмите «Да».\n\n«Нет» - отменить отслеживание."), L.T("Отслеживание установки"))) return;
            await Ui.Busy(this, async ct =>
            {
                var file = Path.Combine(AppPaths.Traces, Stamp() + ".rktrace");
                var changes = await Task.Run(() =>
                {
                    Snapshot.Take(after, "after", roots, ct);
                    var list = Snapshot.Diff(Snapshot.Read(before), Snapshot.Read(after)).ToList();
                    Snapshot.SaveChanges(file, note, list);
                    return list;
                });
                Load();
                Show(changes, note);
            });
        }
        finally
        {
            File.Delete(before);
            File.Delete(after);
        }
    }

    void ShowTrace_Click(object s, RoutedEventArgs e)
    {
        if (TraceList.SelectedItem is not SnapInfo t) return;
        try { Show(Snapshot.LoadChanges(t.File), t.Note); }
        catch (Exception ex) { Ui.Error(ex); }
    }

    async void Rollback_Click(object s, RoutedEventArgs e)
    {
        Changes.CommitEdit();
        var items = _changes.Where(c => c.Checked).ToList();
        if (items.Count == 0 || !Ui.Confirm(L.T($"Откатить {items.Count} изменений ({_source})?\nДобавленные разделы и значения будут удалены, изменённые - возвращены, новые файлы уйдут в корзину. Откат реестра можно отменить в Центре отмены."))) return;
        var errors = new List<string>();
        await Ui.Busy(this, () => Task.Run(() =>
        {
            using var tx = UndoTx.Begin(L.T("Откат: ") + _source);
            Snapshot.Rollback(items, tx);
            errors = tx.Errors;
        }));
        Ui.Info(errors.Count == 0 ? L.T("Готово.") : L.T("Готово, но:\n") + string.Join("\n", errors.Take(20)) + (errors.Count > 20 ? L.T($"\n...и ещё {errors.Count - 20}") : ""));
    }

    void All_Click(object s, RoutedEventArgs e) => SetAll(true);
    void None_Click(object s, RoutedEventArgs e) => SetAll(false);

    void SetAll(bool v)
    {
        foreach (SnapChange c in Changes.Items) c.Checked = v; // only the filtered rows
        Changes.Items.Refresh();
    }

    void Open_Click(object s, RoutedEventArgs e)
    {
        if (Changes.SelectedItem is not SnapChange { Entry: var en }) return;
        if (en.IsRegistry) MainWindow.Current.Show<RegEditorView>().Navigate(en.Path);
        else if (Directory.Exists(en.Path)) Sys.Open(en.Path);
        else Sys.ShowInExplorer(en.Path);
    }

    void Delete_Click(object s, RoutedEventArgs e)
    {
        var sel = _lastList?.SelectedItems.Cast<SnapInfo>().ToList() ?? new();
        if (sel.Count == 0 || !Ui.Confirm(L.T($"Удалить {sel.Count} шт.?"))) return;
        sel.ForEach(i => File.Delete(i.File));
        Load();
    }

    void Folder_Click(object s, RoutedEventArgs e) => Sys.Open(AppPaths.Data);
}

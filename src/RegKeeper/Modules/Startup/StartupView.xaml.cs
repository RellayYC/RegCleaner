using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class StartupView : UserControl
{
    List<StartupItem> _all = new();

    public StartupView()
    {
        InitializeComponent();
        Loaded += async (_, _) => { if (_all.Count == 0) await Reload(); };
    }

    async Task Reload() => await Ui.Busy(this, async () =>
    {
        _all = await Task.Run(Autoruns.Load);
        ApplyFilter();
    });

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        var list = _all.Where(i => (ShowServices.IsChecked == true || i.Kind != StartupKind.Service)
                                   && (ShowTasks.IsChecked == true || i.Kind != StartupKind.Task)
                                   && (q == "" || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                               || i.Command.Contains(q, StringComparison.OrdinalIgnoreCase)
                                               || i.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        Grid.ItemsSource = list;
        Status.Text = $"Записей: {list.Count}   Включено: {list.Count(i => i.Enabled)}   Битых: {list.Count(i => i.Missing)}";
    }

    void Search_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();
    void Filter_Click(object s, RoutedEventArgs e) => ApplyFilter();

    List<StartupItem> Selected => Grid.SelectedItems.Cast<StartupItem>().ToList();

    void Toggle_Click(object s, RoutedEventArgs e)
    {
        var items = Selected.Where(i => i.CanToggle).ToList();
        if (items.Count == 0) return;
        var on = !items[0].Enabled;
        var errors = new List<string>();
        using (var tx = UndoTx.Begin((on ? "Включено в автозагрузке: " : "Отключено в автозагрузке: ") + string.Join(", ", items.Select(i => i.Name))))
        {
            foreach (var i in items)
                try { Autoruns.SetEnabled(i, on, tx); }
                catch (Exception ex) { errors.Add(i.Name + ": " + ex.Message); }
            errors.AddRange(tx.Errors);
        }
        if (errors.Count > 0) Ui.Info("Не удалось:\n" + string.Join("\n", errors));
        Grid.Items.Refresh();
        ApplyFilter();
    }

    async void Delay_Click(object s, RoutedEventArgs e)
    {
        var items = Selected.Where(i => i.Kind is StartupKind.Registry or StartupKind.Folder && i.Approved != null && i.Enabled).ToList();
        if (items.Count == 0) { Ui.Info("Выберите включённые записи реестра или файлы папки автозагрузки."); return; }
        if (Ui.Prompt("Отложенный запуск", "Задержка после входа в Windows, секунд:", "60") is not { } text) return;
        if (!int.TryParse(text, out var sec) || sec is < 1 or > 3600) { Ui.Info("Нужно число от 1 до 3600."); return; }
        var errors = new List<string>();
        using (var tx = UndoTx.Begin($"Отложено на {sec} с: " + string.Join(", ", items.Select(i => i.Name))))
        {
            foreach (var i in items)
                try { Autoruns.Delay(i, TimeSpan.FromSeconds(sec), tx); }
                catch (Exception ex) { errors.Add(i.Name + ": " + ex.Message); }
            errors.AddRange(tx.Errors);
        }
        if (errors.Count > 0) Ui.Info("Не удалось:\n" + string.Join("\n", errors));
        await Reload();
    }

    async void Delete_Click(object s, RoutedEventArgs e)
    {
        var items = Selected;
        if (items.Count == 0 || !Ui.Confirm("Удалить из автозагрузки:\n" + string.Join("\n", items.Select(i => i.Name)) +
                                            "\n\nЗаписи реестра и задачи можно вернуть в Центре отмены, файлы уйдут в корзину.")) return;
        var errors = new List<string>();
        using (var tx = UndoTx.Begin("Удалено из автозагрузки: " + string.Join(", ", items.Select(i => i.Name))))
        {
            foreach (var i in items)
                try { Autoruns.Delete(i, tx); }
                catch (Exception ex) { errors.Add(i.Name + ": " + ex.Message); }
            errors.AddRange(tx.Errors);
        }
        if (errors.Count > 0) Ui.Info("Не удалось:\n" + string.Join("\n", errors));
        await Reload();
    }

    async void Add_Click(object s, RoutedEventArgs e)
    {
        if (Ui.Prompt("Добавить в автозагрузку", "Команда (путь к программе и аргументы):") is not { Length: > 0 } cmd) return;
        if (Ui.Prompt("Добавить в автозагрузку", "Название записи:", Path.GetFileNameWithoutExtension(Sys.ExtractPath(cmd) ?? cmd)) is not { Length: > 0 } name) return;
        var allUsers = Ui.Confirm("Запускать для всех пользователей? (Нет - только для текущего)");
        using (var tx = UndoTx.Begin("Добавлено в автозагрузку: " + name)) Autoruns.Add(name, cmd, allUsers, tx);
        await Reload();
    }

    void Folder_Click(object s, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not StartupItem i) return;
        var path = i.Kind == StartupKind.Folder ? i.Location : Sys.ExtractPath(i.Command);
        if (path != null && File.Exists(path)) Sys.ShowInExplorer(path);
        else Ui.Info("Файл не найден: " + path);
    }

    void Goto_Click(object s, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not StartupItem i) return;
        switch (i.Kind)
        {
            case StartupKind.Registry or StartupKind.Service: MainWindow.Current.Show<RegEditorView>().Navigate(i.Location); break;
            case StartupKind.Folder: Sys.ShowInExplorer(i.Location); break;
            case StartupKind.Task: Sys.Open("taskschd.msc"); break;
        }
    }

    async void Refresh_Click(object s, RoutedEventArgs e) => await Reload();
}

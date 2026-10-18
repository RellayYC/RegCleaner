using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public sealed record ValueRow(RawValue V)
{
    public string Name => V.Name == "" ? L.T("(По умолчанию)") : V.Name;
    public string Type => V.TypeName;
    public string Data => V.Display();
}

/// <summary>One Miller column: the subkeys of <see cref="Parent"/>, <see cref="Selected"/> the one on the path.</summary>
public sealed record MillerCol(string Parent, List<string> Names, string? Selected);
public sealed record MillerRoot(string Long, string Short, bool On);

public partial class RegEditorView : UserControl
{
    static readonly string FavPath = Path.Combine(AppPaths.Data, "favorites.txt");
    string? _path;

    public RegEditorView()
    {
        InitializeComponent();
        L.Apply(this);
        LoadFavorites();
        Go("HKEY_CURRENT_USER");
    }

    string? Current => _path;

    /// <summary>Shows <paramref name="path"/>, an existing key: the rail, the last three levels as columns, the crumbs and the values.</summary>
    void Go(string path)
    {
        _path = path;
        var parts = path.Split('\\');
        Roots.ItemsSource = Reg.RootNames.Select(r => new MillerRoot(r, Reg.Short(r), r == parts[0])).ToList();
        Columns.ItemsSource = Enumerable.Range(Math.Max(0, parts.Length - 3), Math.Min(3, parts.Length)).Select(i =>
        {
            var parent = string.Join('\\', parts.Take(i + 1));
            return new MillerCol(parent, Reg.SubKeys(parent).Order(StringComparer.OrdinalIgnoreCase).ToList(), i + 1 < parts.Length ? parts[i + 1] : null);
        }).ToList();
        Star.Content = _favs.Contains(path) ? "\uE735" : "\uE734";
        ShowValues();
    }

    void ShowValues()
    {
        if (Current is not { } path) return;
        Address.Text = path;
        var parts = path.Split('\\');
        Crumbs.ItemsSource = parts.Select((p, i) => new KeyValuePair<string, string>(string.Join('\\', parts.Take(i + 1)), p)).ToList();
        using var k = Reg.Open(path);
        Values.ItemsSource = k == null ? null : Reg.Values(k).OrderBy(v => v.Name != "").ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Select(v => new ValueRow(v)).ToList();
    }

    // the binding preselects the path item before the column loads; only a later pick is a click
    void Column_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        var lb = (ListBox)s;
        if (lb.IsLoaded && lb.SelectedItem is string name && lb.DataContext is MillerCol c && name != c.Selected) Go(c.Parent + "\\" + name);
    }

    void Column_Loaded(object s, RoutedEventArgs e) { if (((ListBox)s).SelectedItem is { } i) ((ListBox)s).ScrollIntoView(i); }

    /// <summary>Goes to the deepest existing key of <paramref name="path"/>, saying so when it stops short.</summary>
    public void Navigate(string path)
    {
        string full;
        try { full = Reg.Normalize(path); }
        catch (ArgumentException e) { Ui.Info(e.Message); return; }
        var parts = full.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var at = Reg.RootNames.First(r => r.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
        foreach (var part in parts.Skip(1))
        {
            if (Reg.SubKeys(at).FirstOrDefault(n => n.Equals(part, StringComparison.OrdinalIgnoreCase)) is not { } name)
            {
                Ui.Info(L.T($"Раздел не найден или нет доступа:\n{full}\n\nОткрыт ближайший: {at}"));
                break;
            }
            at += "\\" + name;
        }
        Go(at);
    }

    /// <summary>Re-reads the columns and values, then goes to <paramref name="select"/> if given.</summary>
    void Reload(string? select = null)
    {
        if (select != null) Navigate(select);
        else if (_path != null) Navigate(_path);
    }

    /// <summary>Runs a change through an undo transaction and reports what failed.</summary>
    static bool Change(string title, Action<UndoTx> work)
    {
        try
        {
            using var tx = UndoTx.Begin(title);
            work(tx);
            if (tx.Errors.Count > 0) Ui.Info(string.Join("\n", tx.Errors));
            return tx.Errors.Count == 0;
        }
        catch (Exception e) { Ui.Error(e); return false; }
    }

    // the crumb bar turns into a path box: Ctrl+L or a click on the bar; Enter goes, Esc or leaving puts the crumbs back
    void TypePath(bool on)
    {
        Address.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        CrumbView.Visibility = on ? Visibility.Hidden : Visibility.Visible;
        if (!on) return;
        Address.Text = _path;
        Address.Focus();
        Address.SelectAll();
    }

    void View_PreviewKeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { TypePath(true); e.Handled = true; }
    }

    void CrumbBar_MouseLeftButtonUp(object s, MouseButtonEventArgs e) { if (Address.Visibility != Visibility.Visible) TypePath(true); }

    void Address_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        var path = Address.Text;
        TypePath(false);
        if (e.Key == Key.Enter) Navigate(path);
    }

    void Address_LostKeyboardFocus(object s, KeyboardFocusChangedEventArgs e) => TypePath(false);
    void Crumb_Click(object s, RoutedEventArgs e) => Navigate((string)((Button)s).Tag);
    void Clipboard_Click(object s, RoutedEventArgs e) { if (Clipboard.ContainsText()) Navigate(Clipboard.GetText().Trim().Trim('[', ']')); }
    void CopyPath_Click(object s, RoutedEventArgs e) { if (Current != null) Clipboard.SetText(Current); }
    void Refresh_Click(object s, RoutedEventArgs e) => Reload();

    // ---- keys

    void NewKey_Click(object s, RoutedEventArgs e)
    {
        if (Current is not { } path || Ui.Prompt(L.T("Новый раздел"), L.T("Имя раздела:")) is not { Length: > 0 } name) return;
        if (Change(L.T("Редактор: новый раздел"), tx => tx.CreateKey(path + "\\" + name))) Reload(path + "\\" + name);
    }

    void RenameKey_Click(object s, RoutedEventArgs e)
    {
        if (Current is not { } path || !path.Contains('\\')) return;
        if (Ui.Prompt(L.T("Переименовать раздел"), L.T("Новое имя:"), Reg.Leaf(path)) is not { Length: > 0 } name || name == Reg.Leaf(path)) return;
        if (!Ui.Confirm(L.T($"Переименование = копия + удаление. Для больших разделов это долго.\nПереименовать {Reg.Short(path)} в {name}?"))) return;
        if (Change(L.T("Редактор: переименование раздела"), tx => tx.RenameKey(path, name)))
        {
            SelectParent();
            Reload(Reg.Combine(Reg.Parent(path), name));
        }
    }

    void CopyKey_Click(object s, RoutedEventArgs e)
    {
        if (Current is not { } path || !path.Contains('\\')) return;
        if (Ui.Prompt(L.T("Копировать раздел"), L.T("Полный путь копии:"), path + L.T(" - копия")) is not { Length: > 0 } input) return;
        var to = Reg.Normalize(input.Trim().Trim('[', ']'));
        if (to.StartsWith(Reg.Normalize(path) + "\\", StringComparison.OrdinalIgnoreCase)) { Ui.Info(L.T("Копия не может лежать внутри самого раздела.")); return; }
        if (Reg.Exists(to)) { Ui.Info(L.T("Раздел уже существует: ") + to); return; }
        if (Change(L.T("Редактор: копия раздела"), tx => tx.CopyKey(path, to))) Reload(to);
    }

    void DeleteKey_Click(object s, RoutedEventArgs e)
    {
        if (Current is not { } path || !path.Contains('\\')) return;
        if (!Ui.Confirm(L.T($"Удалить раздел со всем содержимым?\n{path}"))) return;
        Change(L.T("Редактор: удаление раздела"), tx => tx.DeleteKey(path));
        SelectParent();
        Reload();
    }

    void SelectParent() { if (_path?.Contains('\\') == true) _path = Reg.Parent(_path); }

    // a whole hive takes minutes to export: off the UI thread, so the window stays alive
    async void Export_Click(object s, RoutedEventArgs e)
    {
        if (Current is not { } path) return;
        var dlg = new SaveFileDialog { Filter = L.T("Файлы реестра (*.reg)|*.reg"), FileName = Reg.Leaf(path) + ".reg" };
        if (dlg.ShowDialog() != true) return;
        await Ui.Busy(this, () => Task.Run(() => RegFile.Save(dlg.FileName, RegFile.Export(new[] { path }))));
    }

    void Import_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = L.T("Файлы реестра (*.reg)|*.reg") };
        if (dlg.ShowDialog() != true) return;
        List<RegOp> ops;
        try { ops = RegFile.Parse(File.ReadAllText(dlg.FileName)); }
        catch (Exception ex) { Ui.Error(ex); return; }
        // preview: every operation of the file, untick what must not be applied
        var rows = ops.Select(o => new Leftover
        {
            Kind = o.Kind switch { RegOpKind.CreateKey => L.T("Создать ключ"), RegOpKind.DeleteKey => L.T("Удалить ключ"), RegOpKind.SetValue => L.T("Значение"), _ => L.T("Удалить значение") },
            Path = o.Kind switch
            {
                RegOpKind.SetValue => $"{o.Key}  :  {(o.Value!.Name == "" ? L.T("(По умолчанию)") : o.Value.Name)} = {o.Value.Display()}",
                RegOpKind.DeleteValue => $"{o.Key}  :  {o.ValueName}",
                _ => o.Key
            }
        }).ToList();
        if (!ChecklistWindow.Ask(L.T("Импорт ") + Path.GetFileName(dlg.FileName), L.T("Что будет изменено в реестре. Откат - в Центре отмены."), rows, L.T("Применить отмеченное"))) return;
        ops = ops.Where((_, i) => rows[i].Checked).ToList();
        if (Change(L.T("Импорт ") + Path.GetFileName(dlg.FileName), tx => RegFile.Apply(ops, tx)) && ops.Count > 0) Reload(ops[0].Key);
    }

    // ---- values

    RawValue? SelectedValue => (Values.SelectedItem as ValueRow)?.V;

    static RawValue? Ask(string title, string name, uint type, string text)
    {
        while (Ui.Prompt(title, $"{name} ({new RawValue("", type, Array.Empty<byte>()).TypeName}):", text, type == RawValue.MultiSz) is { } input)
        {
            try { return ValueText.Parse(name, type, input); }
            catch (Exception e) when (e is FormatException or OverflowException) { Ui.Info(L.T("Неверное значение: ") + e.Message); text = input; }
        }
        return null;
    }

    void NewValue_Click(object s, RoutedEventArgs e)
    {
        if (Current is not { } path || Ui.Prompt(L.T("Новое значение"), L.T("Имя:")) is not { } name) return;
        if (Reg.Read(path, name) != null) { Ui.Info(L.T("Значение с таким именем уже есть.")); return; }
        var type = uint.Parse((string)((ComboBoxItem)NewType.SelectedItem).Tag);
        if (Ask(L.T("Новое значение"), name, type, type is RawValue.Dword or RawValue.Qword ? "0" : "") is { } v
            && Change(L.T("Редактор: новое значение"), tx => tx.SetValue(path, v))) ShowValues();
    }

    void EditValue_Click(object s, RoutedEventArgs e)
    {
        if (Current is not { } path || SelectedValue is not { } old) return;
        if (Ask(L.T("Изменить значение"), old.Name, old.Type, ValueText.ToText(old)) is { } v
            && Change(L.T("Редактор: изменение значения"), tx => tx.SetValue(path, v))) ShowValues();
    }

    void RenameValue_Click(object s, RoutedEventArgs e)
    {
        if (Current is not { } path || SelectedValue is not { Name.Length: > 0 } old) return;
        if (Ui.Prompt(L.T("Переименовать значение"), L.T("Новое имя:"), old.Name) is not { Length: > 0 } name || name == old.Name) return;
        if (Reg.Read(path, name) != null) { Ui.Info(L.T("Значение с таким именем уже есть.")); return; }
        if (Change(L.T("Редактор: переименование значения"), tx => { tx.SetValue(path, old with { Name = name }); tx.DeleteValue(path, old.Name); })) ShowValues();
    }

    void DeleteValue_Click(object s, RoutedEventArgs e)
    {
        if (Current is not { } path) return;
        var names = Values.SelectedItems.Cast<ValueRow>().Select(r => r.V.Name).ToList();
        if (names.Count == 0 || !Ui.Confirm(L.T($"Удалить значений: {names.Count}?"))) return;
        Change(L.T("Редактор: удаление значений"), tx => names.ForEach(n => tx.DeleteValue(path, n)));
        ShowValues();
    }

    // ---- favorites: one path per line

    List<string> _favs = new();

    void LoadFavorites()
    {
        _favs = File.Exists(FavPath) ? File.ReadAllLines(FavPath).Where(l => l != "").ToList() : new List<string>();
        FavMenu.Items.Clear();
        foreach (var p in _favs)
        {
            var item = new MenuItem { Header = Reg.Short(p) };
            item.Click += (_, _) => Navigate(p);
            FavMenu.Items.Add(item);
        }
        FavMenu.IsEnabled = _favs.Count > 0;
    }

    void AddFavorite_Click(object s, RoutedEventArgs e)
    {
        if (Current is not { } path) return;
        if (!_favs.Remove(path)) _favs.Add(path);
        File.WriteAllLines(FavPath, _favs);
        LoadFavorites();
        Go(path);
    }
}

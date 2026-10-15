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

public partial class RegEditorView : UserControl
{
    static readonly string FavPath = Path.Combine(AppPaths.Data, "favorites.txt");
    const string Dummy = "…";

    public RegEditorView()
    {
        InitializeComponent();
        L.Apply(this);
        foreach (var root in Reg.RootNames) Tree.Items.Add(Node(root, root));
        LoadFavorites();
    }

    string? Current => (Tree.SelectedItem as TreeViewItem)?.Tag as string;

    static TreeViewItem Node(string header, string path)
    {
        var n = new TreeViewItem { Header = header, Tag = path };
        n.Items.Add(Dummy);
        n.Expanded += (_, _) => Fill(n);
        return n;
    }

    /// <summary>Lazy load: the dummy child gives every key an expander; real children replace it on first expand.</summary>
    static void Fill(TreeViewItem n, bool force = false)
    {
        if (!force && !(n.Items.Count == 1 && n.Items[0] is string)) return;
        var path = (string)n.Tag;
        n.Items.Clear();
        foreach (var sub in Reg.SubKeys(path).Order(StringComparer.OrdinalIgnoreCase)) n.Items.Add(Node(sub, path + "\\" + sub));
    }

    void Tree_SelectedItemChanged(object s, RoutedPropertyChangedEventArgs<object> e) => ShowValues();

    void ShowValues()
    {
        if (Current is not { } path) return;
        Address.Text = path;
        using var k = Reg.Open(path);
        Values.ItemsSource = k == null ? null : Reg.Values(k).OrderBy(v => v.Name != "").ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Select(v => new ValueRow(v)).ToList();
    }

    /// <summary>Expands the tree down to the deepest existing key of <paramref name="path"/> and selects it.</summary>
    public void Navigate(string path)
    {
        string full;
        try { full = Reg.Normalize(path); }
        catch (ArgumentException e) { Ui.Info(e.Message); return; }
        var parts = full.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var node = Tree.Items.Cast<TreeViewItem>().First(n => ((string)n.Tag).Equals(parts[0], StringComparison.OrdinalIgnoreCase));
        foreach (var part in parts.Skip(1))
        {
            Fill(node);
            node.IsExpanded = true;
            // a key created since this node was loaded (copy target, another program): re-read once before giving up
            if ((Child(node, part) ?? (Reg.Exists(node.Tag + "\\" + part) ? Refill(node, part) : null)) is not { } next)
            {
                Ui.Info(L.T($"Раздел не найден или нет доступа:\n{full}\n\nОткрыт ближайший: {node.Tag}"));
                break;
            }
            node = next;
        }
        node.IsSelected = true;
        node.BringIntoView();
        Dispatcher.BeginInvoke(() => node.BringIntoView(), System.Windows.Threading.DispatcherPriority.Background);
    }

    static TreeViewItem? Child(TreeViewItem n, string name) =>
        n.Items.OfType<TreeViewItem>().FirstOrDefault(c => ((string)c.Header).Equals(name, StringComparison.OrdinalIgnoreCase));

    static TreeViewItem? Refill(TreeViewItem n, string name) { Fill(n, true); return Child(n, name); }

    /// <summary>Re-reads the selected node's children (keeps expansion of the node itself) and its values.</summary>
    void Reload(string? select = null)
    {
        if (Tree.SelectedItem is TreeViewItem n) { Fill(n, true); n.IsExpanded = true; }
        if (select != null) Navigate(select);
        else ShowValues();
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

    void Address_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Enter) Navigate(Address.Text); }
    void Go_Click(object s, RoutedEventArgs e) => Navigate(Address.Text);
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

    void SelectParent()
    {
        if (Tree.SelectedItem is TreeViewItem { Parent: TreeViewItem p }) p.IsSelected = true;
    }

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

    void LoadFavorites() => Favorites.ItemsSource = File.Exists(FavPath) ? File.ReadAllLines(FavPath).Where(l => l != "").ToList() : new List<string>();

    void AddFavorite_Click(object s, RoutedEventArgs e)
    {
        if (Current is not { } path) return;
        var list = (List<string>)Favorites.ItemsSource;
        if (!list.Remove(path)) list.Add(path);
        File.WriteAllLines(FavPath, list);
        LoadFavorites();
    }

    void Favorites_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (Favorites.SelectedItem is string p) Navigate(p);
        Favorites.SelectedItem = null;
    }
}

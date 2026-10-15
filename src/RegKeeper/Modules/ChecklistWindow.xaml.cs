using System.Windows;
using RegKeeper.Core;

namespace RegKeeper.Modules;

/// <summary>"Here is what we found, tick what to remove" dialog. Returns true when the user confirmed.</summary>
public partial class ChecklistWindow : Window
{
    readonly List<Leftover> _items;

    public ChecklistWindow(string title, string header, List<Leftover> items, string okText = "Удалить отмеченное")
    {
        InitializeComponent();
        Theme.TitleBar(this);
        Owner = Application.Current.MainWindow;
        Title = title;
        Header.Text = header;
        OkButton.Content = okText;
        _items = items;
        Grid.ItemsSource = items;
    }

    public static bool Ask(string title, string header, List<Leftover> items, string okText = "Удалить отмеченное") =>
        new ChecklistWindow(title, header, items, okText).ShowDialog() == true;

    void All_Click(object s, RoutedEventArgs e) => SetAll(true);
    void None_Click(object s, RoutedEventArgs e) => SetAll(false);

    void SetAll(bool v)
    {
        _items.ForEach(i => i.Checked = v);
        Grid.Items.Refresh();
    }

    void Reveal_Click(object s, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not Leftover l) return;
        if (l.IsRegistry) { MainWindow.Current.Show<RegEditorView>().Navigate(l.Path); Close(); }
        else if (Directory.Exists(l.Path)) Sys.Open(l.Path);
        else Sys.ShowInExplorer(l.Path);
    }

    void Ok_Click(object s, RoutedEventArgs e) => DialogResult = true;
}

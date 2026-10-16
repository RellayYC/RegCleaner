using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class StoreAppsView : UserControl
{
    List<StoreApp> _all = new();
    List<StoreApp> _shown = new();

    public StoreAppsView()
    {
        InitializeComponent();
        L.Apply(this);
        Loaded += async (_, _) => { if (_all.Count == 0) await Reload(); };
    }

    async Task Reload()
    {
        try
        {
            await Ui.Busy(this, async () => _all = await Task.Run(() =>
            {
                var apps = StoreApps.Load();
                StoreApps.FillSizes(apps);
                var favs = Favorites;
                foreach (var a in apps)
                {
                    a.Icon = StoreApps.Logo(a.InstallLocation);
                    a.Favorite = favs.Contains(a.Name);
                }
                return apps.OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            }));
        }
        catch (Exception e) { Ui.Error(e); }
        MainWindow.SetBadge(typeof(StoreAppsView), _all.Count(a => !a.IsSystem).ToString());
        ApplyFilter();
    }

    const string Key = MainWindow.Key;

    /// <summary>Package names the user starred, HKCU\Software\RegKeeper\StoreFavorites.</summary>
    static HashSet<string> Favorites
    {
        get => new(Registry.GetValue(Key, "StoreFavorites", null) as string[] ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        set => Registry.SetValue(Key, "StoreFavorites", value.ToArray(), RegistryValueKind.MultiString);
    }

    // the figures describe what the system filter shows; the search narrows only the table
    List<StoreApp> Basis => _all.Where(a => ShowSystem.IsChecked == true || !a.IsSystem).ToList();

    void ApplyFilter()
    {
        var basis = Basis;
        var max = Math.Max(1, basis.Count == 0 ? 1 : basis.Max(a => a.SizeBytes));
        foreach (var a in basis) a.Rel = (double)a.SizeBytes / max;
        var q = Search.Text.Trim();
        // OrderBy is stable: favourites first, the rest keep the title order
        _shown = basis.Where(a => q == "" || (a.Title + " " + a.Name + " " + a.PublisherName).Contains(q, StringComparison.OrdinalIgnoreCase))
                      .OrderBy(a => !a.Favorite).ToList();
        var selected = Selected;
        Grid.ItemsSource = _shown;
        if (selected != null && _shown.Contains(selected)) Grid.SelectedItem = selected;
        UpdateFigures(basis);
        UpdateDetails();
    }

    void UpdateFigures(List<StoreApp> basis)
    {
        var total = basis.Sum(a => a.SizeBytes);
        var bySize = basis.Where(a => a.SizeBytes > 0).OrderByDescending(a => a.SizeBytes).ToList();

        AppsFigure.Text = basis.Count.ToString();
        var hidden = _all.Count - basis.Count;
        AppsNote.Text = hidden > 0 ? L.T($"ещё {hidden} системных скрыто") : L.T("включая системные");

        SetFigure(SpaceFigure, total);
        var top = bySize.Take(5).Sum(a => a.SizeBytes);
        var share = total > 0 ? (double)top / total : 0;
        DrawRing(share);
        RingText.Text = $"{share * 100:0}%";
        SpaceNote.Text = L.T("доля 5 крупнейших");

        var heavy = bySize.Where(a => a.Heavy).ToList();
        HeavyFigure.Text = heavy.Count.ToString();
        HeavyBars.Children.Clear();
        for (var i = 0; i < 2; i++) HeavyBars.Children.Add(Bar(i < heavy.Count ? heavy[i] : null));

        if (long.TryParse(Reg.GetString(Key, "LastFreeable"), out var free) && free > 0)
        {
            SetFigure(FreeFigure, free);
            FreeNote.Text = L.T("по последнему анализу «Очистки диска»");
        }
        else
        {
            FreeFigure.Text = "-";
            FreeNote.Text = L.T("Запустите анализ в «Очистке диска»");
        }

        SpaceTotal.Text = L.T($"всего {Sys.Size(total)}");
        DrawSegments(bySize, total);
    }

    // the unit goes smaller and grey next to the number, as on the other figures
    static void SetFigure(TextBlock t, long bytes)
    {
        var text = Sys.Size(bytes);
        var cut = text.LastIndexOf(' ');
        t.Inlines.Clear();
        t.Inlines.Add(new Run(cut > 0 ? text[..cut] : text));
        if (cut <= 0) return;
        var unit = new Run(" " + text[(cut + 1)..]) { FontSize = 20 };
        unit.SetResourceReference(TextElement.ForegroundProperty, "MutedBrush");
        t.Inlines.Add(unit);
    }

    void DrawRing(double share)
    {
        const double c = 28, r = 25; // 56 box, 6 stroke
        if (share >= 0.999) { Ring.Data = new EllipseGeometry(new Point(c, c), r, r); return; }
        if (share <= 0) { Ring.Data = null; return; }
        var a = share * 2 * Math.PI;
        var end = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = new Point(c, c - r) };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, share > 0.5, SweepDirection.Clockwise, true));
        Ring.Data = new PathGeometry(new[] { fig });
    }

    // one red bar per heavy app, empty track when there is none
    static FrameworkElement Bar(StoreApp? app)
    {
        var g = new System.Windows.Controls.Grid { Height = 6, Margin = new Thickness(0, 0, 0, 8), ToolTip = app == null ? null : $"{app.Title} - {app.SizeText}" };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(app == null ? 0 : Math.Max(app.Rel, 0.04), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(app == null ? 1 : 1 - Math.Max(app.Rel, 0.04), GridUnitType.Star) });
        var track = new Border { CornerRadius = new CornerRadius(3) };
        track.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
        System.Windows.Controls.Grid.SetColumnSpan(track, 2);
        g.Children.Add(track);
        if (app == null) return g;
        var bar = new Border { CornerRadius = new CornerRadius(3) };
        bar.SetResourceReference(Border.BackgroundProperty, "HotBarBrush");
        bar.SetResourceReference(EffectProperty, "GlowHot");
        g.Children.Add(bar);
        return g;
    }

    // largest first: white, red, three greys, then everything else in one dark segment
    static readonly string[] SegBrushes = { "SegFirstBrush", "HotBarBrush", "Seg3Brush", "Seg4Brush", "Seg5Brush" };

    void DrawSegments(List<StoreApp> bySize, long total)
    {
        Segments.Children.Clear();
        Segments.ColumnDefinitions.Clear();
        Legend.Children.Clear();
        if (total <= 0) return;
        var parts = bySize.Take(5).Select((a, i) => (a.Title, a.SizeBytes, SegBrushes[i])).ToList();
        var rest = total - parts.Sum(p => p.SizeBytes);
        if (rest > 0) parts.Add((L.T("Другие"), rest, "SegOtherBrush"));
        for (var i = 0; i < parts.Count; i++)
        {
            var (name, size, brush) = parts[i];
            Segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(size, total / 200.0), GridUnitType.Star) });
            var seg = new Border { CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, i < parts.Count - 1 ? 4 : 0, 0), ToolTip = $"{name} - {Sys.Size(size)}" };
            seg.SetResourceReference(Border.BackgroundProperty, brush);
            if (i < 2) seg.SetResourceReference(EffectProperty, i == 0 ? "GlowLight" : "GlowHot");
            System.Windows.Controls.Grid.SetColumn(seg, i);
            Segments.Children.Add(seg);

            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Border.BackgroundProperty, brush);
            var label = new TextBlock { Text = name, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            var sz = new TextBlock { Text = Sys.Size(size), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sz.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            Legend.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 26, 8), Children = { dot, label, sz } });
        }
    }

    StoreApp? Selected => Grid.SelectedItem as StoreApp;
    // ticked rows win; without ticks the highlighted rows are the target
    List<StoreApp> Targets => _shown.Where(a => a.Checked).ToList() is { Count: > 0 } c ? c : Grid.SelectedItems.Cast<StoreApp>().ToList();

    void Grid_SelectionChanged(object s, SelectionChangedEventArgs e) => UpdateDetails();

    void UpdateDetails()
    {
        var app = Selected;
        DetailsEmpty.Visibility = app == null ? Visibility.Visible : Visibility.Collapsed;
        DetailsBody.Visibility = app == null ? Visibility.Collapsed : Visibility.Visible;
        if (app == null) return;
        DIcon.Source = app.Icon;
        DLetter.Text = app.Icon == null ? app.Letter : "";
        DName.Text = app.Title;
        DPublisher.Text = app.PublisherName;
        DSize.Text = app.SizeBytes > 0 ? app.SizeText : "-";
        var total = Basis.Sum(a => a.SizeBytes);
        var share = total > 0 ? (double)app.SizeBytes / total : 0;
        DShare.Text = L.T($"{share * 100:0.#}% от общего");
        DShareFill.Width = new GridLength(Math.Max(share, 0.01), GridUnitType.Star);
        DShareRest.Width = new GridLength(1 - Math.Max(share, 0.01), GridUnitType.Star);
        DPackage.Text = app.PackageFullName;
        DVersion.Text = app.Version;
        DType.Text = app.IsFramework ? L.T("Фреймворк")
            : app.IsSystem ? L.T("Системное")
            : app.SignatureKind == "Store" ? "Microsoft Store" : L.T("Стороннее");
        DNote.Text = app.IsSystem ? L.T("Системный пакет: удаление заблокировано.")
            : L.T("Удалённое приложение можно вернуть только переустановкой из Microsoft Store.");
    }

    // widgets wrap to two columns and the details go under the table on a narrow window
    void Page_ScrollChanged(object s, ScrollChangedEventArgs e)
    {
        // the grid's own scroller bubbles this event too
        if (e.OriginalSource != Page || (e.ViewportHeightChange == 0 && e.ViewportWidthChange == 0)) return;
        var w = Page.ViewportWidth - Body.Margin.Right;
        Widgets.Columns = w >= 974 ? 4 : 2;
        var under = w < 980;
        System.Windows.Controls.Grid.SetColumn(Details, under ? 0 : 1);
        System.Windows.Controls.Grid.SetRow(Details, under ? 1 : 0);
        Details.Width = under ? double.NaN : Math.Clamp(w * 0.3, 340, 420);
        Details.Margin = under ? new Thickness(0, 18, 0, 0) : new Thickness(18, 0, 0, 0);
        // the table keeps a fixed height so the grid virtualizes its rows inside the scrolling page
        TableCard.Height = Math.Max(360, Page.ViewportHeight - 24);
    }

    void Search_TextChanged(object s, RoutedEventArgs e) => ApplyFilter();
    // Checked/Unchecked, not Click: UI Automation toggles do not raise Click
    void Filter_Changed(object s, RoutedEventArgs e) { if (IsInitialized) ApplyFilter(); }

    void Grid_ContextMenuOpening(object s, ContextMenuEventArgs e)
    {
        if (Selected is not { } app) { e.Handled = true; return; }
        MenuCheck.Header = app.Checked ? L.T("Снять выбор") : L.T("Выбрать");
        MenuFav.Header = app.Favorite ? L.T("Убрать из избранного") : L.T("В избранное");
        MenuFav.Tag = app.Favorite ? "" : "";
        var n = _shown.Count(a => a.Checked);
        MenuRemove.Header = n > 0 ? L.T($"Удалить отмеченные ({n})") : L.T("Удалить");
        MenuRun.IsEnabled = StoreApps.Aumid(app) != null;
    }

    void Check_Click(object s, RoutedEventArgs e)
    {
        if (Selected is not { } app) return;
        app.Checked = !app.Checked;
        Grid.Items.Refresh();
    }

    void Fav_Click(object s, RoutedEventArgs e)
    {
        if (Selected is not { } app) return;
        var favs = Favorites;
        if (app.Favorite = !app.Favorite) favs.Add(app.Name); else favs.Remove(app.Name);
        Favorites = favs;
        ApplyFilter();
        Grid.SelectedItem = app;
        Grid.ScrollIntoView(app);
    }

    // ponytail: explorer.exe starts it as the signed-in user, not with our admin token
    void Run_Click(object s, RoutedEventArgs e)
    {
        if (Selected is { } app && StoreApps.Aumid(app) is { } id) Process.Start("explorer.exe", $"shell:AppsFolder\\{id}");
    }

    void Details_Click(object s, RoutedEventArgs e) => Details.BringIntoView();

    async void Remove_Click(object s, RoutedEventArgs e) => await Remove(Targets);
    async void RemoveSelected_Click(object s, RoutedEventArgs e) { if (Selected is { } app) await Remove(new() { app }); }

    async Task Remove(List<StoreApp> apps)
    {
        if (apps.Count == 0) return;
        if (apps.FirstOrDefault(a => a.IsSystem) is { } sys) { Ui.Info(L.T($"«{sys.Title}» - системный пакет, его удалять нельзя.")); return; }
        if (!Ui.Confirm(L.T("Удалить приложения?\n") + string.Join("\n", apps.Select(a => a.Title)) + L.T("\n\nВернуть их можно только из Microsoft Store."))) return;
        var errors = new List<string>();
        await Ui.Busy(this, () => Task.Run(() =>
        {
            foreach (var a in apps)
                if (StoreApps.Remove(a) is { } err) errors.Add($"{a.Title}: {err}");
        }));
        if (errors.Count > 0) Ui.Info(L.T("Не удалось:\n") + string.Join("\n", errors));
        await Reload();
    }

    void Folder_Click(object s, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is StoreApp { InstallLocation: { } dir } && Directory.Exists(dir)) Sys.Open(dir);
    }

    async void Refresh_Click(object s, RoutedEventArgs e) => await Reload();
}

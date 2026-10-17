using System.Windows;
using System.Windows.Controls;
using RegKeeper.Core;

namespace RegKeeper.Modules;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        L.Apply(this);
        (Theme.Saved switch { Theme.Light => ThemeLight, Theme.Dark => ThemeDark, _ => ThemeSystem }).IsChecked = true;
        foreach (var p in Theme.Scales)
        {
            var r = new RadioButton { Content = p + " %", Tag = p, IsChecked = p == Theme.Scale };
            r.Checked += Scale_Checked;
            ScaleChoices.Children.Add(r);
        }
        Loaded += (_, _) => Motion.Slide(ScalePill, ScaleChoices.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true));
        (L.Saved switch { L.Ru => LangRu, L.En => LangEn, _ => LangAuto }).IsChecked = true;
        (MainWindow.CloseToTray ? CloseTray : CloseExit).IsChecked = true;
        Lite.IsChecked = Motion.LiteSaved;
        Loaded += (_, _) => BackupCard.IsChecked = MainWindow.ShowBackupCard; // the cross on the card may have hidden it since
        ShellVerb.IsChecked = Integration.ShellVerb;
        Watcher.IsChecked = Integration.WatcherAutostart;
        Watcher.IsEnabled = File.Exists(Integration.WatcherExe);
        if (!Watcher.IsEnabled)
        {
            Watcher.ToolTip = L.T("Не найден ") + Integration.WatcherExe;
            ToolTipService.SetShowOnDisabled(Watcher, true);
        }
        DataInfo.Text = L.T($"{AppPaths.Data}\nЦентр отмены, снимки и трассы установок: {Sys.Size(Sys.DirSize(AppPaths.Data))}");
        About.Text = $"RegKeeper {Update.Current}, .NET {Environment.Version}";
        UpdateInfo.Text = Update.Online
            ? L.T("Обновления приходят с сервера RegKeeper, перед установкой проверяется подпись.")
            : L.T("Обновления берутся из папки с установщиками RegKeeper-x.y.z-x64.msi: ") + (Update.Source ?? L.T("папка ещё не выбрана"));
    }

    async void Update_Click(object s, RoutedEventArgs e)
    {
        if (!Update.Online) { UpdateFromFolder(); return; }
        var info = UpdateInfo.Text;
        await Ui.Busy(UpdateBtn, async ct =>
        {
            if (await Update.Check(ct) is not { } r)
            {
                Ui.Info(L.T($"Установлена последняя версия {Update.Current}."));
                return;
            }
            if (!Ui.Confirm(L.T($"Вышла версия {r.Version}. Скачать и установить? RegKeeper закроется примерно на минуту и откроется уже новым."))) return;
            var msi = await Update.Download(r, new Progress<int>(p => UpdateInfo.Text = L.T($"Скачивание: {p} %")), ct);
            Launch(msi);
        });
        UpdateInfo.Text = info;
    }

    // no channel in this build: the newest MSI in the saved folder, asking for a folder when there is none or nothing new in it
    void UpdateFromFolder()
    {
        var folder = Update.Source;
        while (true)
        {
            if (folder == null)
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = L.T("Папка с установщиками RegKeeper") };
                if (dlg.ShowDialog() != true) return;
                Update.Source = folder = dlg.FolderName;
                UpdateInfo.Text = L.T("Обновления берутся из папки с установщиками RegKeeper-x.y.z-x64.msi: ") + folder;
            }
            if (Update.Find(folder) is { } msi)
            {
                if (Ui.Confirm(L.T($"Есть версия {Update.VersionOf(msi)}. Установить? RegKeeper закроется примерно на минуту и откроется уже новым."))) Launch(msi);
                return;
            }
            if (!Ui.Confirm(L.T($"В папке {folder} нет версии новее {Update.Current}. Выбрать другую папку?"))) return;
            folder = null;
        }
    }

    void UpdateFile_Click(object s, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = L.T("Установщик RegKeeper (*.msi)|*.msi") };
        if (dlg.ShowDialog() == true && Ui.Confirm(L.T($"Установить {Path.GetFileName(dlg.FileName)}? RegKeeper закроется примерно на минуту и откроется уже новым.")))
            Launch(dlg.FileName);
    }

    static void Launch(string msi)
    {
        try
        {
            Update.Run(msi);
            ((MainWindow)Application.Current.MainWindow).Quit();
        }
        catch (Exception ex) { Ui.Error(ex); }
    }


    void Theme_Checked(object s, RoutedEventArgs e)
    {
        Motion.Slide(ThemePill, (FrameworkElement)s);
        var mode = (string)((RadioButton)s).Tag;
        if (mode == Theme.Saved) return; // the initial IsChecked from the constructor
        Theme.Saved = mode;
        Theme.Apply(Theme.Saved);
    }

    void Scale_Checked(object s, RoutedEventArgs e)
    {
        Motion.Slide(ScalePill, (FrameworkElement)s);
        Theme.ApplyScale(Theme.Scale = (int)((RadioButton)s).Tag);
    }

    void Close_Checked(object s, RoutedEventArgs e)
    {
        Motion.Slide(ClosePill, (FrameworkElement)s);
        if (MainWindow.CloseToTray != (s == CloseTray)) MainWindow.CloseToTray = s == CloseTray;
    }

    void BackupCard_Changed(object s, RoutedEventArgs e) => MainWindow.ShowBackupCard = BackupCard.IsChecked == true;

    void Lite_Changed(object s, RoutedEventArgs e)
    {
        if (Lite.IsChecked == Motion.LiteSaved) return; // the initial IsChecked from the constructor
        Motion.LiteSaved = Lite.IsChecked == true;
        if (Ui.Confirm(L.T("Лёгкий режим сменится после перезапуска. Перезапустить RegKeeper сейчас?")))
        {
            System.Diagnostics.Process.Start(Environment.ProcessPath!);
            ((MainWindow)Application.Current.MainWindow).Quit();
        }
    }

    void Lang_Checked(object s, RoutedEventArgs e)
    {
        Motion.Slide(LangPill, (FrameworkElement)s);
        var mode = (string)((RadioButton)s).Tag;
        if (mode == L.Saved) return;
        L.Saved = mode;
        if (IsLoaded) Ui.Info(L.T("Язык интерфейса сменится после перезапуска RegKeeper."));
    }

    // Checked/Unchecked (UI Automation toggles raise no Click); also fired by the constructor and the reverts below
    void ShellVerb_Changed(object s, RoutedEventArgs e)
    {
        if (ShellVerb.IsChecked == Integration.ShellVerb) return;
        try { Integration.SetShellVerb(ShellVerb.IsChecked == true); }
        catch (Exception ex) { Ui.Error(ex); }
        ShellVerb.IsChecked = Integration.ShellVerb;
    }

    void Watcher_Changed(object s, RoutedEventArgs e)
    {
        if (Watcher.IsChecked == Integration.WatcherAutostart) return;
        try
        {
            Integration.SetWatcherAutostart(Watcher.IsChecked == true);
            if (Watcher.IsChecked == true) Integration.StartWatcher();
        }
        catch (Exception ex) { Ui.Error(ex); }
        Watcher.IsChecked = Integration.WatcherAutostart;
    }

    void Data_Click(object s, RoutedEventArgs e) => Sys.Open(AppPaths.Data);
}

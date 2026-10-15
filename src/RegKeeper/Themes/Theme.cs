using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using RegKeeper.Core;

namespace RegKeeper;

/// <summary>Light/dark colour theme: "System" follows the Windows app mode. The choice lives in HKCU\Software\RegKeeper\Theme.</summary>
public static class Theme
{
    public const string System = "System", Light = "Light", Dark = "Dark";
    const string Key = @"HKEY_CURRENT_USER\Software\RegKeeper", Personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static string Saved
    {
        get => Reg.GetString(Key, "Theme") is { } t && t is Light or Dark ? t : System;
        set => Registry.SetValue(Key, "Theme", value);
    }

    /// <summary>Light or Dark for a saved mode.</summary>
    public static string Resolve(string mode) =>
        mode is Light or Dark ? mode : Registry.GetValue(Personalize, "AppsUseLightTheme", 1) is 0 ? Dark : Light;

    public static string Current { get; private set; } = Light;

    public static void Apply(string mode)
    {
        Current = Resolve(mode);
        Application.Current.Resources.MergedDictionaries[0] =
            new ResourceDictionary { Source = new Uri($"pack://application:,,,/RegKeeper;component/Themes/{Current}.xaml") };
        foreach (Window w in Application.Current.Windows) TitleBar(w);
    }

    /// <summary>Dark caption to match the theme (Windows 10 2004+, ignored elsewhere).</summary>
    public static void TitleBar(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) { w.SourceInitialized += (_, _) => TitleBar(w); return; }
        int dark = Current == Dark ? 1 : 0;
        DwmSetWindowAttribute(h, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
        var theme = new ResourceDictionary { Source = new Uri($"pack://application:,,,/RegKeeper;component/Themes/{Current}.xaml") };
        if (Motion.Lite) Motion.Strip(theme);
        Application.Current.Resources.MergedDictionaries[0] = theme;
        foreach (Window w in Application.Current.Windows) TitleBar(w);
    }

    public static readonly int[] Scales = { 90, 100, 110, 125, 150 };

    /// <summary>Interface size in percent, HKCU\Software\RegKeeper\Scale.</summary>
    public static int Scale
    {
        get => Reg.GetString(Key, "Scale") is { } s && int.TryParse(s, out var p) && Scales.Contains(p) ? p : 100;
        set => Registry.SetValue(Key, "Scale", value.ToString());
    }

    /// <summary>Every window lays its content out through the UiScale resource (the Window template), so this resizes them all at once.</summary>
    public static void ApplyScale(int percent)
    {
        var t = new ScaleTransform(percent / 100.0, percent / 100.0);
        t.Freeze();
        Application.Current.Resources["UiScale"] = t;
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

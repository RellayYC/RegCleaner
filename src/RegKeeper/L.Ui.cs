using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace RegKeeper;

public static partial class L
{
    /// <summary>Translates the literal text of a freshly loaded view or window: content, headers, tooltips, columns.</summary>
    public static void Apply(DependencyObject root)
    {
        if (Current == Ru) return;
        Walk(root);
    }

    // Template content (grid cells, list items) is built after Apply walked the view, so its literals go through these:
    // <TextBlock rk:L.Text="..."/>, <Button rk:L.Tip="..."/> (the tip also names the element for screen readers)
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached("Text", typeof(string), typeof(L),
        new(null, (d, e) => ((TextBlock)d).Text = T((string)e.NewValue)));
    public static void SetText(DependencyObject d, string v) => d.SetValue(TextProperty, v);
    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);

    public static readonly DependencyProperty TipProperty = DependencyProperty.RegisterAttached("Tip", typeof(string), typeof(L),
        new(null, (d, e) =>
        {
            var s = T((string)e.NewValue);
            ((FrameworkElement)d).ToolTip = s;
            System.Windows.Automation.AutomationProperties.SetName(d, s);
        }));
    public static void SetTip(DependencyObject d, string v) => d.SetValue(TipProperty, v);
    public static string GetTip(DependencyObject d) => (string)d.GetValue(TipProperty);

    static void Walk(object node)
    {
        switch (node)
        {
            case Window w: w.Title = T(w.Title); break;
            case TextBlock t when !BindingOperations.IsDataBound(t, TextBlock.TextProperty):
                if (t.Inlines.Count == 1 || t.Inlines.Count == 0) t.Text = T(t.Text);
                else foreach (var run in t.Inlines.OfType<System.Windows.Documents.Run>().Where(r => !BindingOperations.IsDataBound(r, System.Windows.Documents.Run.TextProperty)).ToList()) run.Text = T(run.Text);
                break;
            case DataGrid g:
                foreach (var c in g.Columns) if (c.Header is string h) c.Header = T(h);
                break;
        }
        if (node is HeaderedContentControl { Header: string hc } hcc) hcc.Header = T(hc);
        if (node is HeaderedItemsControl { Header: string hi } hic) hic.Header = T(hi);
        if (node is ContentControl { Content: string s } cc && !BindingOperations.IsDataBound(cc, ContentControl.ContentProperty)) cc.Content = T(s);
        if (node is FrameworkElement fe)
        {
            if (fe.ToolTip is string tip) fe.ToolTip = T(tip);
            if (fe.ContextMenu != null) Walk(fe.ContextMenu);
        }
        if (node is DependencyObject d)
            foreach (var child in LogicalTreeHelper.GetChildren(d)) Walk(child);
    }
}

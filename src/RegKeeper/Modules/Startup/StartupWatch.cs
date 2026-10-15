namespace RegKeeper.Modules;

/// <summary>Core of RegKeeper.Watcher: which Run/Startup-folder entries appeared since the previous look.</summary>
public static class StartupWatch
{
    public static string Id(StartupItem i) => $"{i.Location}|{i.Name}|{i.Command}";

    public static HashSet<string> Ids(IEnumerable<StartupItem> items) => new(items.Select(Id), StringComparer.OrdinalIgnoreCase);

    public static List<StartupItem> Added(IEnumerable<StartupItem> current, HashSet<string> previous) =>
        current.Where(i => !previous.Contains(Id(i))).ToList();
}

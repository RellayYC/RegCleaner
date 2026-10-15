using System.Runtime.CompilerServices;

namespace RegKeeper.Tests;

static class TestData
{
    // runs before any test touches AppPaths: undo records, snapshots and traces of tests go to %TEMP%
    [ModuleInitializer]
    internal static void Init() => Environment.SetEnvironmentVariable("RK_DATA", Path.Combine(Path.GetTempPath(), "RegKeeperTestData"));
}

public class TestDataTests
{
    [Fact]
    public void Tests_DoNotWriteToTheRealUndoCenter() =>
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "RegKeeperTestData"), RegKeeper.Core.AppPaths.Undo);
}

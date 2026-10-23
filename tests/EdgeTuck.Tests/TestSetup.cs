using EdgeTuck.Shell;

namespace EdgeTuck.Tests;

[TestClass]
public static class TestSetup
{
    /// <summary>Tests of corrupt files log warnings; they must not create %APPDATA%\EdgeTuck\logs on the machine.</summary>
    [AssemblyInitialize]
    public static void Init(TestContext _) => Log.Off = true;
}

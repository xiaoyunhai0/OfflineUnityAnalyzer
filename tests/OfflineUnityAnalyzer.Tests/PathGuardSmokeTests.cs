using OfflineUnityAnalyzer.Core.Configuration;
using OfflineUnityAnalyzer.Core.Safety;
using Xunit;

namespace OfflineUnityAnalyzer.Tests;

public sealed class PathGuardSmokeTests
{
    [Fact]
    public void OutputInsideInputRootIsRejected()
    {
        var inputRoot = CreateTempDirectory("oua-pathguard-input");
        var outputRoot = Path.Combine(inputRoot, "AnalyzerOutput");

        try
        {
            Assert.Throws<ReadonlyViolationException>(() => CreateGuard(inputRoot, outputRoot));
        }
        finally
        {
            DeleteDirectory(inputRoot);
        }
    }

    public static PathGuard CreateGuard(string inputRoot, string outputRoot)
    {
        var config = new AnalyzerConfig
        {
            CodeRoots = new[] { inputRoot },
            Output = outputRoot
        };

        return PathGuard.Create(config);
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

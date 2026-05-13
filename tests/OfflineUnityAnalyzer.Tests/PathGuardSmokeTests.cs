using OfflineUnityAnalyzer.Core.Configuration;
using OfflineUnityAnalyzer.Core.Safety;

namespace OfflineUnityAnalyzer.Tests;

public static class PathGuardSmokeTests
{
    public static PathGuard CreateGuard(string inputRoot, string outputRoot)
    {
        var config = new AnalyzerConfig
        {
            CodeRoots = new[] { inputRoot },
            Output = outputRoot
        };

        return PathGuard.Create(config);
    }
}

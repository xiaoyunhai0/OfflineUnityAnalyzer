using OfflineUnityAnalyzer.Core.Safety;

namespace OfflineUnityAnalyzer.ReportExport;

public sealed class StaticReportExporter
{
    public void WritePlaceholderReport(ISafeFileSystem fileSystem)
    {
        const string html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>OfflineUnityAnalyzer Report</title>
  <link rel="stylesheet" href="assets/style.css">
</head>
<body>
  <main>
    <h1>OfflineUnityAnalyzer</h1>
    <p>The static report UI will be generated in a later milestone.</p>
  </main>
</body>
</html>
""";

        fileSystem.WriteAllTextToOutput("report/report.html", html);
    }
}

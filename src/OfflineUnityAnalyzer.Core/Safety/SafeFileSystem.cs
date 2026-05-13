using System.Text;

namespace OfflineUnityAnalyzer.Core.Safety;

public sealed class SafeFileSystem : ISafeFileSystem
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly PathGuard _pathGuard;

    public SafeFileSystem(PathGuard pathGuard)
    {
        _pathGuard = pathGuard;
    }

    public void EnsureOutputRootExists()
    {
        Directory.CreateDirectory(_pathGuard.OutputRoot);
    }

    public string ReadAllText(string path)
    {
        _pathGuard.EnsureCanRead(path);
        return File.ReadAllText(path, Encoding.UTF8);
    }

    public byte[] ReadAllBytes(string path)
    {
        _pathGuard.EnsureCanRead(path);
        return File.ReadAllBytes(path);
    }

    public Stream OpenRead(string path)
    {
        _pathGuard.EnsureCanRead(path);
        return File.OpenRead(path);
    }

    public void WriteAllTextToOutput(string relativePath, string content)
    {
        var outputPath = _pathGuard.GetOutputPath(relativePath);
        EnsureOutputDirectory(outputPath);
        File.WriteAllText(outputPath, content, Utf8NoBom);
    }

    public void WriteBytesToOutput(string relativePath, byte[] bytes)
    {
        var outputPath = _pathGuard.GetOutputPath(relativePath);
        EnsureOutputDirectory(outputPath);
        File.WriteAllBytes(outputPath, bytes);
    }

    public Stream CreateOutput(string relativePath)
    {
        var outputPath = _pathGuard.GetOutputPath(relativePath);
        EnsureOutputDirectory(outputPath);
        return File.Create(outputPath);
    }

    private static void EnsureOutputDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}

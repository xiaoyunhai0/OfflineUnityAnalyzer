namespace OfflineUnityAnalyzer.Core.Safety;

public interface ISafeFileSystem
{
    void EnsureOutputRootExists();

    string ReadAllText(string path);

    byte[] ReadAllBytes(string path);

    Stream OpenRead(string path);

    void WriteAllTextToOutput(string relativePath, string content);

    void WriteBytesToOutput(string relativePath, byte[] bytes);

    Stream CreateOutput(string relativePath);
}

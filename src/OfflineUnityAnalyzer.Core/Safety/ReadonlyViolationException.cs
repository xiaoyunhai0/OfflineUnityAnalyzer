namespace OfflineUnityAnalyzer.Core.Safety;

public sealed class ReadonlyViolationException : Exception
{
    public ReadonlyViolationException(string message) : base(message)
    {
    }
}

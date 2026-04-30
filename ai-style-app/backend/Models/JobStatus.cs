namespace AiStyleApp.Api.Models;

public static class JobStatus
{
    public const string Queued     = "Queued";
    public const string Processing = "Processing";
    public const string Succeeded  = "Succeeded";
    public const string Failed     = "Failed";
    public const string TimedOut   = "TimedOut";
    public const string Canceled   = "Canceled";

    public static readonly IReadOnlySet<string> Terminal = new HashSet<string>
    {
        Succeeded, Failed, TimedOut, Canceled
    };

    public static bool IsTerminal(string status) => Terminal.Contains(status);
}

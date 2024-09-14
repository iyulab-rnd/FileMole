using System.Diagnostics;

namespace FileMoles;

public class Logger
{
    internal static void Info(string message)
    {
        Debug.WriteLine(message);
    }

    internal static void Error(string message)
    {
        Debug.WriteLine(message);
    }

    internal static void Error(Exception ex, string? message = null)
    {
#if DEBUG
        message ??= ex.Message;
        Debug.WriteLine(message);
        Debugger.Break();
#endif
    }
}

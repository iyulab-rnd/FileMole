namespace FileMoles;

public class Logger
{
    internal static void Debug(string message, params object?[] args)
    {
        System.Diagnostics.Debug.WriteLine(message, args);
    }

    internal static void Info(string message, params object?[] args)
    {
        System.Diagnostics.Debug.WriteLine(message, args);
    }

    internal static void Error(string message, params object?[] args)
    {
        System.Diagnostics.Debug.WriteLine(message, args);
    }

    internal static void Error(Exception ex, string? message = null, params object?[] args)
    {
#if DEBUG
        message ??= ex.Message;
        System.Diagnostics.Debug.WriteLine(message, args);
        //Debugger.Break();
#endif
    }
}

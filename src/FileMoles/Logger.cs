using System.Diagnostics;

namespace FileMoles;

public class Logger
{
    internal static void WriteLine(string message)
    {
        Debug.WriteLine(message);
    }
    internal static void OnException(Exception ex)
    {
        Debug.WriteLine(ex.Message);
    }
}

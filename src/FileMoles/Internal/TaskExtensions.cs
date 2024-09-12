namespace FileMoles.Internal;

internal static class TaskExtensions
{
    public static async void Forget(this Task task)
    {
        await task;
    }
}
namespace FileMole.Utils
{
    public static class TaskExtensions
    {
        public static async void Forget(this Task task)
        {
            await task;
        }
    }
}
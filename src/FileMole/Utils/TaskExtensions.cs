namespace FileMole.Utils
{
    public static class TaskExtensions
    {
        public static void Forget(this Task task)
        {
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    // 예외를 그대로 발생시킵니다.
                    var exception = t.Exception.InnerException ?? t.Exception;
                    throw exception;
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
namespace FileMoles;

public class MonitoringOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan DebounceDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    public bool IncludeSubdirectories { get; set; } = true;
    public NotifyFilters NotifyFilters { get; set; } = NotifyFilters.LastWrite
        | NotifyFilters.FileName
        | NotifyFilters.DirectoryName;
    public int MaxConcurrentWatchers { get; set; } = 100;
    public int MaxQueueSize { get; set; } = 1000;
    public TimeSpan MaxProcessingDelay { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    public void Validate()
    {
        if (MaxConcurrentWatchers <= 0)
            throw new ArgumentException("MaxConcurrentWatchers must be positive");

        if (MaxQueueSize <= 0)
            throw new ArgumentException("MaxQueueSize must be positive");

        if (MaxProcessingDelay <= TimeSpan.Zero)
            throw new ArgumentException("MaxProcessingDelay must be positive");

        if (DebounceDelay <= TimeSpan.Zero)
            throw new ArgumentException("DebounceDelay must be positive");

        if (MaxRetries <= 0)
            throw new ArgumentException("MaxRetries must be positive");

        if (RetryDelay <= TimeSpan.Zero)
            throw new ArgumentException("RetryDelay must be positive");
    }
}

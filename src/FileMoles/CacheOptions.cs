namespace FileMoles;

public class CacheOptions
{
    private long _maxCacheSize = 1024L * 1024L * 1024L; // 1GB
    private TimeSpan _expiration = TimeSpan.FromMinutes(10);
    private int _maxConcurrentOperations = 100;

    public bool Enabled { get; set; } = true;
    public string ConnectionString { get; set; }

    public TimeSpan Expiration
    {
        get => _expiration;
        set
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentException("Expiration must be positive", nameof(value));
            _expiration = value;
        }
    }

    public long MaxCacheSize
    {
        get => _maxCacheSize;
        set
        {
            if (value <= 0)
                throw new ArgumentException("MaxCacheSize must be positive", nameof(value));
            _maxCacheSize = value;
        }
    }

    public int MaxConcurrentOperations
    {
        get => _maxConcurrentOperations;
        set
        {
            if (value <= 0)
                throw new ArgumentException("MaxConcurrentOperations must be positive", nameof(value));
            _maxConcurrentOperations = value;
        }
    }

    public bool CompressData { get; set; } = false;
    public int CommandTimeout { get; set; } = 30;

    public void Validate()
    {
        if (Enabled && string.IsNullOrEmpty(ConnectionString))
            throw new InvalidOperationException("ConnectionString is required when cache is enabled");

        if (MaxCacheSize < 1024 * 1024) // 1MB minimum
            throw new InvalidOperationException("MaxCacheSize must be at least 1MB");

        if (Expiration < TimeSpan.FromSeconds(1))
            throw new InvalidOperationException("Expiration must be at least 1 second");
    }
}

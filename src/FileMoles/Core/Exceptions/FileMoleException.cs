namespace FileMoles.Core.Exceptions;

public class FileMoleException : Exception
{
    public FileMoleException(string message) : base(message) { }
    public FileMoleException(string message, Exception inner) : base(message, inner) { }
}

public class ProviderNotFoundException : FileMoleException
{
    public string ProviderId { get; }
    public ProviderNotFoundException(string providerId)
        : base($"Storage provider '{providerId}' not found.")
    {
        ProviderId = providerId;
    }
}

public class InvalidPathException : FileMoleException
{
    public string Path { get; }
    public InvalidPathException(string path, string message)
        : base($"Invalid path '{path}': {message}")
    {
        Path = path;
    }
}

public class StorageOperationException : FileMoleException
{
    public string Operation { get; }
    public string ProviderId { get; }
    public string Path { get; }

    public StorageOperationException(
        string operation,
        string providerId,
        string path,
        string message,
        Exception innerException = null)
        : base($"Storage operation '{operation}' failed for {providerId}:{path}: {message}",
            innerException)
    {
        Operation = operation;
        ProviderId = providerId;
        Path = path;
    }
}

public class InvalidProviderConfigurationException : FileMoleException
{
    public string ProviderId { get; }
    public string ConfigurationKey { get; }

    public InvalidProviderConfigurationException(
        string providerId,
        string configurationKey,
        string message)
        : base($"Invalid configuration for provider '{providerId}': {message}")
    {
        ProviderId = providerId;
        ConfigurationKey = configurationKey;
    }
}

public class CacheOverflowException : FileMoleException
{
    public long CurrentSize { get; }
    public long MaxSize { get; }

    public CacheOverflowException(long currentSize, long maxSize)
        : base($"Cache size limit exceeded: {currentSize} > {maxSize}")
    {
        CurrentSize = currentSize;
        MaxSize = maxSize;
    }
}
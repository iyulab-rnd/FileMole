namespace FileMoles;

public class FileMoleOptions
{
    public CacheOptions Cache { get; set; } = new();
    public MonitoringOptions Monitoring { get; set; } = new();
    public SecurityOptions Security { get; set; } = new();
}

public class SecurityOptions
{
    public bool EnforcePathSecurity { get; set; } = true;
    public bool CheckAccessRights { get; set; } = true;
    public string[] AllowedPaths { get; set; } = Array.Empty<string>();
    public string[] BlockedExtensions { get; set; } = Array.Empty<string>();
}
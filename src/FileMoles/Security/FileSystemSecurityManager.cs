using FileMoles.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FileMoles.Security;

public class FileSystemSecurityManager
{
    private readonly SecurityOptions _options;
    private readonly ILogger<FileSystemSecurityManager> _logger;

    public FileSystemSecurityManager(
        IOptions<SecurityOptions> options,
        ILogger<FileSystemSecurityManager> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool ValidateAccess(string path)
    {
        if (!_options.EnforcePathSecurity)
            return true;

        try
        {
            if (!IsAllowedPath(path))
            {
                _logger.LogWarning("Access denied to blocked path: {Path}", path);
                return false;
            }

            if (HasBlockedExtension(path))
            {
                _logger.LogWarning("Access denied to blocked extension: {Path}", path);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating access for {Path}", path);
            return false;
        }
    }

    private bool IsAllowedPath(string path)
    {
        if (_options.AllowedPaths.Length == 0)
            return true;

        // 크로스 플랫폼 경로 정규화
        var normalizedPath = Path.GetFullPath(path)
            .Replace('\\', '/')
            .TrimEnd('/');

        return _options.AllowedPaths.Any(allowedPath =>
        {
            var normalizedAllowedPath = Path.GetFullPath(allowedPath)
                .Replace('\\', '/')
                .TrimEnd('/');

            return normalizedPath.StartsWith(normalizedAllowedPath,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    private bool HasBlockedExtension(string path)
    {
        if (_options.BlockedExtensions.Length == 0)
            return false;

        var extension = Path.GetExtension(path);
        return _options.BlockedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}

public class SecurityOptions
{
    public bool EnforcePathSecurity { get; set; } = true;
    public string[] AllowedPaths { get; set; } = Array.Empty<string>();
    public string[] BlockedExtensions { get; set; } = Array.Empty<string>();
}
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FileMoles.Common.Utils;

public static class PathUtils
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly bool IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    private static readonly char PlatformSeparator = Path.DirectorySeparatorChar;

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "/";

        // Replace Windows backslashes with forward slashes
        path = path.Replace('\\', '/');

        // Remove consecutive slashes
        path = Regex.Replace(path, "/+", "/");

        // Remove trailing slashes except for root
        path = path.TrimEnd('/');
        if (string.IsNullOrEmpty(path))
            return "/";

        // Handle Windows drive letters
        if (IsWindows && IsDrivePath(path))
        {
            // Convert "C:" to "C:/"
            if (path.Length == 2)
                return path + "/";

            // Ensure drive letter format is consistent
            path = char.ToUpperInvariant(path[0]) + path.Substring(1);
        }

        // Ensure leading slash for non-Windows or if not a drive path
        if (!IsWindows || !IsDrivePath(path))
        {
            if (!path.StartsWith("/"))
                path = "/" + path;
        }

        return path;
    }

    public static string GetParentPath(string path)
    {
        var normalized = NormalizePath(path);

        if (normalized == "/" ||
            (IsWindows && IsDrivePath(normalized) && normalized.Length <= 3))
            return null;

        var lastSeparator = normalized.LastIndexOf('/');
        if (lastSeparator <= 0)
            return "/";

        // Handle Windows drive letters
        if (IsWindows && IsDrivePath(normalized))
        {
            if (lastSeparator <= 2)
                return normalized.Substring(0, 3);
        }

        return normalized.Substring(0, lastSeparator);
    }

    public static string CombinePath(params string[] paths)
    {
        if (paths == null || paths.Length == 0)
            return "/";

        var normalizedParts = new List<string>();

        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path))
                continue;

            var normalizedPath = path.Trim('/');

            // Handle Windows drive letters
            if (IsWindows && IsDrivePath(normalizedPath))
            {
                normalizedParts.Clear(); // Drive letter starts a new absolute path
                normalizedParts.Add(normalizedPath);
                continue;
            }

            if (normalizedPath != ".")
                normalizedParts.Add(normalizedPath);
        }

        var result = string.Join("/", normalizedParts);

        // Handle Windows drive letters in the result
        if (IsWindows && IsDrivePath(result))
            return result.Length == 2 ? result + "/" : result;

        return "/" + result;
    }

    public static bool IsSubPathOf(string basePath, string path)
    {
        var normalizedBase = NormalizePath(basePath);
        var normalizedPath = NormalizePath(path);

        // Handle case sensitivity based on platform
        var comparison = IsCaseSensitive()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return normalizedPath.StartsWith(normalizedBase, comparison)
            && normalizedPath.Length > normalizedBase.Length;
    }

    public static string GetRelativePath(string basePath, string path)
    {
        var normalizedBase = NormalizePath(basePath);
        var normalizedPath = NormalizePath(path);
        var comparison = IsCaseSensitive()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (!normalizedPath.StartsWith(normalizedBase, comparison))
            throw new ArgumentException("Path is not under the base path", nameof(path));

        var relativePath = normalizedPath
            .Substring(normalizedBase.Length)
            .TrimStart('/');

        return string.IsNullOrEmpty(relativePath) ? "." : relativePath;
    }

    public static bool IsValidPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        try
        {
            // Check for invalid characters based on platform
            var invalidChars = IsWindows ?
                Path.GetInvalidPathChars() :
                new char[] { '\0' }; // Unix only considers null character invalid

            if (path.IndexOfAny(invalidChars) >= 0)
                return false;

            // Additional Windows-specific checks
            if (IsWindows)
            {
                // Check for reserved names (CON, PRN, AUX, etc.)
                var fileName = Path.GetFileName(path);
                if (IsWindowsReservedName(fileName))
                    return false;

                // Check drive letter format
                if (IsDrivePath(path) && !IsValidDriveLetter(path[0]))
                    return false;
            }

            // Verify path can be resolved
            _ = Path.GetFullPath(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string SanitizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        // Remove invalid characters based on platform
        var invalidChars = IsWindows ?
            Path.GetInvalidPathChars() :
            new char[] { '\0' };

        var sanitized = new string(path.Where(c => !invalidChars.Contains(c)).ToArray());

        // Replace potentially problematic characters with underscores
        sanitized = Regex.Replace(sanitized, @"[<>:""|?*]", "_");

        // Remove consecutive slashes
        sanitized = Regex.Replace(sanitized, "/+", "/");

        return sanitized;
    }

    private static bool IsDrivePath(string path)
    {
        return path.Length >= 2 &&
               char.IsLetter(path[0]) &&
               path[1] == ':';
    }

    private static bool IsValidDriveLetter(char letter)
    {
        return letter >= 'A' && letter <= 'Z' ||
               letter >= 'a' && letter <= 'z';
    }

    private static bool IsCaseSensitive()
    {
        return !IsWindows && !IsMacOS; // Only Linux/Unix are case-sensitive
    }

    private static bool IsWindowsReservedName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        var reservedNames = new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        return reservedNames.Contains(name.ToUpperInvariant());
    }
}
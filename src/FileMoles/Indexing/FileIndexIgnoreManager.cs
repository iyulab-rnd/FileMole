using GlobExpressions;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileMoles.Indexing;

internal class FileIndexIgnoreManager
{
    private readonly List<Glob> ignorePatterns;

    public FileIndexIgnoreManager()
    {
        string ignoreContents = @"
*.tmp
*.bak
*.log
*.db
*.db-journal
*.db-wal
*.db-shm
**/bin/**
**/obj/**
**/node_modules/**
**/AppData/**
";
        ignorePatterns = ignoreContents
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(pattern => new Glob(pattern.Trim(), GlobOptions.Compiled))
            .ToList();
    }

    internal bool ShouldIgnore(string fullPath)
    {
        // Normalize the path to use forward slashes
        string normalizedPath = fullPath.Replace('\\', '/').TrimEnd('/');

        // Get just the file name
        string fileName = Path.GetFileName(normalizedPath);

        return ignorePatterns.Any(glob =>
            glob.IsMatch(fileName) || // Match against file name
            glob.IsMatch(normalizedPath) // Match against full path
        );
    }
}
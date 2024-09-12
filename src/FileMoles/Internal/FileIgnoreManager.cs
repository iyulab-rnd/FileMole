using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace FileMoles.Internal;

internal class FileIgnoreManager
{
    private readonly ConcurrentDictionary<string, bool> _ignoredPaths = new();
    private readonly ConcurrentDictionary<string, Regex> _ignoredPatterns = new();

    public FileIgnoreManager(string datPath)
    {
        var configPath = Path.Combine(datPath, Constants.MonitoringIgnoreFileName);
        LoadIgnoreFile(configPath);
        AddDefaultIgnores();
    }

    private void LoadIgnoreFile(string path)
    {
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
                {
                    AddIgnorePattern(line.Trim());
                }
            }
        }
    }

    private void AddDefaultIgnores()
    {
        var defaultIgnores = new[]
        {
            "filemole*", "*.db-journal", ".git", ".svn", ".hg", ".vs", ".vscode",
            "*.suo", "*.user", "*.userosscache", "*.sln.docstates", "bin", "obj",
            "packages", "node_modules", "*.log", "*.sqlite", ".DS_Store", "Thumbs.db",
            "*.tmp", "*.temp", "~$*", "*.bak", "*.swp", "project.lock.json",
            "project.fragment.lock.json", "artifacts", "*.cache", "*.orig"
        };

        foreach (var ignore in defaultIgnores)
        {
            AddIgnorePattern(ignore);
        }
    }

    public void AddIgnorePattern(string pattern)
    {
        if (Path.IsPathRooted(pattern))
        {
            _ignoredPaths[Path.GetFullPath(pattern)] = true;
        }
        else
        {
            _ignoredPatterns[pattern] = new Regex(WildcardToRegex(pattern), RegexOptions.IgnoreCase);
        }
    }

    public bool ShouldIgnore(string path)
    {
        if (_ignoredPaths.ContainsKey(Path.GetFullPath(path)))
            return true;

        var fileName = Path.GetFileName(path);
        return _ignoredPatterns.Values.Any(regex => regex.IsMatch(fileName) || regex.IsMatch(path));
    }

    private static string WildcardToRegex(string pattern)
    {
        return "^" + Regex.Escape(pattern)
                          .Replace("\\*", ".*")
                          .Replace("\\?", ".") + "$";
    }
}
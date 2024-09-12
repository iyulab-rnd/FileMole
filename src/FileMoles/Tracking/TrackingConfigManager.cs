using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FileMoles.Internal;

namespace FileMoles.Tracking;

public class TrackingConfigManager
{
    private readonly string _ignoreConfigPath;
    private readonly string _includeConfigPath;
    private readonly ConcurrentDictionary<string, bool> _ignorePatterns = new();
    private readonly ConcurrentDictionary<string, bool> _includePatterns = new();
    private readonly List<string> _defaultExcludePatterns = new()
    {
        "System Volume Information", "$RECYCLE.BIN", "RECYCLER",
        "*.tmp", "*.temp", "~$*", "*.log", "*.cache", "*.bak", "*.backup",
        ".git", ".svn", ".hg", ".vs", ".vscode", "*.suo", "*.user",
        "bin", "obj", "node_modules", "packages",
        "filemole*", "*.db-journal"
    };

    public TrackingConfigManager(string dataPath)
    {
        _ignoreConfigPath = Path.Combine(dataPath, Constants.TrackingIgnoreFileName);
        _includeConfigPath = Path.Combine(dataPath, Constants.TrackingIncludeFileName);
        IOHelper.CreateDirectory(dataPath);
        LoadPatterns();
    }

    public void AddIgnorePattern(string pattern)
    {
        _ignorePatterns[pattern] = true;
        SavePatterns(_ignoreConfigPath, _ignorePatterns.Keys);
    }

    public void AddIncludePattern(string pattern)
    {
        _includePatterns[pattern] = true;
        SavePatterns(_includeConfigPath, _includePatterns.Keys);
    }

    public bool ShouldTrackFile(string filePath)
    {
        if (IOHelper.IsHidden(filePath)) return false;

        string fileName = Path.GetFileName(filePath);
        if (_defaultExcludePatterns.Any(pattern => IsMatch(fileName, pattern))) return false;

        if (_includePatterns.Any())
        {
            return _includePatterns.Keys.Any(pattern => IsMatch(filePath, pattern)) &&
                   !_ignorePatterns.Keys.Any(pattern => IsMatch(filePath, pattern));
        }

        return !_ignorePatterns.Keys.Any(pattern => IsMatch(filePath, pattern));
    }

    private static bool IsMatch(string input, string pattern)
    {
        pattern = pattern.ToLowerInvariant().Replace('\\', '/');
        input = input.ToLowerInvariant().Replace('\\', '/');

        string regex = "^" + Regex.Escape(pattern)
                                 .Replace("\\*", ".*")
                                 .Replace("\\?", ".") + "$";

        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase);
    }

    private static void SavePatterns(string configPath, IEnumerable<string> patterns)
    {
        var normalizedPatterns = patterns.Select(p => p.Replace('\\', Path.DirectorySeparatorChar)
                                                       .Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllLines(configPath, normalizedPatterns);
    }

    private void LoadPatterns()
    {
        if (File.Exists(_ignoreConfigPath))
        {
            foreach (var pattern in File.ReadAllLines(_ignoreConfigPath))
            {
                _ignorePatterns[pattern] = true;
            }
        }

        if (File.Exists(_includeConfigPath))
        {
            foreach (var pattern in File.ReadAllLines(_includeConfigPath))
            {
                _includePatterns[pattern] = true;
            }
        }

        foreach (var pattern in _defaultExcludePatterns)
        {
            _ignorePatterns[pattern.Replace('\\', '/')] = true;
        }
    }

    public IReadOnlyList<string> GetIgnorePatterns() => _ignorePatterns.Keys.ToList();
    public IReadOnlyList<string> GetIncludePatterns() => _includePatterns.Keys.ToList();

    public void ClearIgnorePatterns()
    {
        _ignorePatterns.Clear();
        foreach (var pattern in _defaultExcludePatterns)
        {
            _ignorePatterns[pattern] = true;
        }
        SavePatterns(_ignoreConfigPath, _ignorePatterns.Keys.Except(_defaultExcludePatterns));
    }

    public void ClearIncludePatterns()
    {
        _includePatterns.Clear();
        SavePatterns(_includeConfigPath, Array.Empty<string>());
    }

    public bool RemoveIgnorePattern(string pattern)
    {
        if (_ignorePatterns.TryRemove(pattern, out _) && !_defaultExcludePatterns.Contains(pattern))
        {
            SavePatterns(_ignoreConfigPath, _ignorePatterns.Keys.Except(_defaultExcludePatterns));
            return true;
        }
        return false;
    }

    public bool RemoveIncludePattern(string pattern)
    {
        if (_includePatterns.TryRemove(pattern, out _))
        {
            SavePatterns(_includeConfigPath, _includePatterns.Keys);
            return true;
        }
        return false;
    }
}
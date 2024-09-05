using System.Text.RegularExpressions;

namespace FileMoles;

public class ConfigManager
{
    private readonly string _configPath;
    private readonly List<string> _ignorePatterns = new List<string>();
    private readonly List<string> _includePatterns = new List<string>();
    private readonly string _ignoreConfigPath;
    private readonly string _includeConfigPath;

    public ConfigManager(string configPath)
    {
        _configPath = configPath;
        _ignoreConfigPath = Path.Combine(_configPath, ".tracking-ignore");
        _includeConfigPath = Path.Combine(_configPath, ".tracking-include");
        Directory.CreateDirectory(_configPath);
        LoadPatterns();
    }

    public void AddIgnorePattern(string pattern)
    {
        _ignorePatterns.Add(pattern);
        SavePatterns(_ignoreConfigPath, _ignorePatterns);
    }

    public void AddIncludePattern(string pattern)
    {
        _includePatterns.Add(pattern);
        SavePatterns(_includeConfigPath, _includePatterns);
    }

    public bool ShouldTrackFile(string filePath)
    {
        if (_includePatterns.Count > 0)
        {
            if (!_includePatterns.Any(pattern => Regex.IsMatch(filePath, WildcardToRegex(pattern))))
            {
                return false;
            }
        }

        return !_ignorePatterns.Any(pattern => Regex.IsMatch(filePath, WildcardToRegex(pattern)));
    }

    private static string WildcardToRegex(string pattern)
    {
        return "^" + Regex.Escape(pattern)
                          .Replace("\\*", ".*")
                          .Replace("\\?", ".") + "$";
    }

    private static void SavePatterns(string configPath, List<string> patterns)
    {
        File.WriteAllLines(configPath, patterns);
    }

    private void LoadPatterns()
    {
        if (File.Exists(_ignoreConfigPath))
        {
            _ignorePatterns.AddRange(File.ReadAllLines(_ignoreConfigPath));
        }

        if (File.Exists(_includeConfigPath))
        {
            _includePatterns.AddRange(File.ReadAllLines(_includeConfigPath));
        }
    }

    public List<string> GetIgnorePatterns() => new List<string>(_ignorePatterns);
    public List<string> GetIncludePatterns() => new List<string>(_includePatterns);

    public void ClearIgnorePatterns()
    {
        _ignorePatterns.Clear();
        SavePatterns(_ignoreConfigPath, _ignorePatterns);
    }

    public void ClearIncludePatterns()
    {
        _includePatterns.Clear();
        SavePatterns(_includeConfigPath, _includePatterns);
    }

    public bool RemoveIgnorePattern(string pattern)
    {
        bool removed = _ignorePatterns.Remove(pattern);
        if (removed)
        {
            SavePatterns(_ignoreConfigPath, _ignorePatterns);
        }
        return removed;
    }

    public bool RemoveIncludePattern(string pattern)
    {
        bool removed = _includePatterns.Remove(pattern);
        if (removed)
        {
            SavePatterns(_includeConfigPath, _includePatterns);
        }
        return removed;
    }
}
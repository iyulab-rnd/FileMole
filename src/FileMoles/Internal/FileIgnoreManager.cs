using GlobExpressions;
using System.Diagnostics;
using System.Text;

namespace FileMoles.Internal;

#if DEBUG
public class FileIgnoreManager
#else
internal class FileIgnoreManager
#endif
{
    private readonly List<Glob> _globs = [];
    private readonly string _configPath;
    private readonly string _dataPath;
    private readonly List<string> _patterns = [];

    public FileIgnoreManager(string dataPath)
    {
        _dataPath = dataPath;
        _configPath = Path.Combine(dataPath, Constants.IgnoreFileName);
        if (!File.Exists(_configPath))
        {
            File.WriteAllText(_configPath, GetDefaultIgnoresText());
        }
        LoadIgnoreFile(_configPath);
    }

    private void LoadIgnoreFile(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var trimmedLine = line.Trim();
            if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith("#"))
            {
                AddPattern(trimmedLine);
            }
        }
    }

    public void AddIgnorePattern(string pattern)
    {
        AddPattern(pattern);
        File.AppendAllText(_configPath, Environment.NewLine + pattern);
    }

    private void AddPattern(string pattern)
    {
        string adjustedPattern = 
            pattern.EndsWith('/') 
            ? $"**/{pattern}**" 
            : $"**/{pattern}";
        _globs.Add(new Glob(adjustedPattern, GlobOptions.CaseInsensitive));
        _patterns.Add(adjustedPattern);
    }

    public bool ShouldIgnore(string path)
    {
        string fullPath = Path.GetFullPath(Path.Combine(_dataPath, path));
        if (IsHiddenFileOrDirectory(fullPath))
        {
            return true;
        }
        string relativePath = Path.GetRelativePath(_dataPath, fullPath).Replace('\\', '/');

        Debug.WriteLine($"Checking path: {relativePath}");
        Debug.WriteLine($"Patterns: {string.Join(", ", _patterns)}");

        return _globs.Any(glob =>
        {
            bool isMatch = glob.IsMatch(relativePath);
            if (isMatch) Debug.WriteLine($"Matched pattern: {glob}");
            return isMatch;
        });
    }

    public string GetPatternsDebugInfo() =>
        $"Current patterns:{Environment.NewLine}{string.Join(Environment.NewLine, _patterns.Select(p => $"- {p}"))}";

    private bool IsHiddenFileOrDirectory(string path)
    {
        foreach (var part in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Skip(1))
        {
            path = Path.Combine(path, part);
            if (IsHiddenEntry(path))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsHiddenEntry(string path)
    {
        if (Path.GetFileName(path).StartsWith(".", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Attributes.HasFlag(FileAttributes.Hidden);
            }
            if (Directory.Exists(path))
            {
                var dirInfo = new DirectoryInfo(path);
                return dirInfo.Parent != null && dirInfo.Attributes.HasFlag(FileAttributes.Hidden);
            }
        }
        catch
        {
            // 접근 권한 문제 등으로 인한 예외 발생 시 무시
        }

        return false;
    }

    private static string GetDefaultIgnoresText() => @"# Generals
*.tmp
*.temp
*.bak
*.swp
*~
*.log
logs/

# Coding
node_modules/
build/
dist/
bin/
obj/
packages/

# Database
*.db
*.sqlite
*.sqlite3
*.mdf
*.ldf
";
}
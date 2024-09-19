using FileMoles.Internal;
using GlobExpressions;

namespace FileMoles.Monitoring;

public class MonitoringFileIgnoreManager
{
    private readonly List<Glob> _globs = [];
    private readonly string _configPath;
    private readonly string _dataPath;
    private readonly List<string> _patterns = [];

    public MonitoringFileIgnoreManager(string dataPath)
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
            if (!string.IsNullOrEmpty(trimmedLine) && !trimmedLine.StartsWith('#'))
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
        bool isIncludePattern = pattern.StartsWith('!');
        string adjustedPattern = isIncludePattern
            ? pattern[1..]  // '!' 제거 후 패턴 적용
            : pattern;

        adjustedPattern = adjustedPattern.EndsWith('/')
            ? $"**/{adjustedPattern}**"
            : $"**/{adjustedPattern}";

        var globPattern = new Glob(adjustedPattern, GlobOptions.CaseInsensitive);

        if (isIncludePattern)
        {
            // '!' 패턴은 무시 리스트에서 제외할 항목이므로, 가장 마지막에 추가
            _globs.Insert(0, globPattern);
        }
        else
        {
            _globs.Add(globPattern);
        }

        _patterns.Add((isIncludePattern ? "!" : "") + adjustedPattern);
    }

    public bool ShouldIgnore(string path)
    {
        string fullPath = Path.GetFullPath(Path.Combine(_dataPath, path));
        if (IsHiddenFileOrDirectory(fullPath))
        {
            return true;
        }
        string relativePath = Path.GetRelativePath(_dataPath, fullPath).Replace('\\', '/');

        // 포함 패턴을 우선 처리
        foreach (var glob in _globs)
        {
            bool isMatch = glob.IsMatch(relativePath);
            if (isMatch)
            {
                // 포함 패턴이면 무시하지 않음
                if (_patterns[_globs.IndexOf(glob)].StartsWith('!'))
                {
                    return false;
                }
                // 무시 패턴이면 true 반환
                return true;
            }
        }

        return false;
    }

    public string GetPatternsDebugInfo() =>
        $"Current patterns:{Environment.NewLine}{string.Join(Environment.NewLine, _patterns.Select(p => $"- {p}"))}";

    private static bool IsHiddenFileOrDirectory(string path)
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
*.db-*
*.sqlite
*.sqlite3
*.mdf
*.ldf

# System
AppData/
";
}
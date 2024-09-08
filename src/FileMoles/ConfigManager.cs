using FileMoles.Internals;
using FileMoles.Utils;
using System.Text.RegularExpressions;

namespace FileMoles;

public class ConfigManager
{
    private readonly string _configPath;
    private readonly List<string> _ignorePatterns = new();
    private readonly List<string> _includePatterns = new();
    private readonly string _ignoreConfigPath;
    private readonly string _includeConfigPath;

    // 기본 제외 패턴 목록
    private readonly List<string> _defaultExcludePatterns = new()
    {
        // 시스템 및 숨김 파일/폴더
        "System Volume Information",
        "$RECYCLE.BIN",
        "RECYCLER",

        // 임시 파일
        "*.tmp",
        "*.temp",
        "~$*",

        // 로그 파일
        "*.log",

        // 캐시 파일
        "*.cache",

        // 백업 파일
        "*.bak",
        "*.backup",

        // 버전 관리 시스템 폴더
        ".git",
        ".svn",
        ".hg",

        // IDE 및 에디터 관련 파일/폴더
        ".vs",
        ".vscode",
        "*.suo",
        "*.user",

        // 빌드 결과물
        "bin",
        "obj",

        // 패키지 관리자 폴더
        "node_modules",
        "packages",

        // FileMole 관련 파일
        "filemole*",
        "*.db-journal"
    };

    public ConfigManager(string configPath)
    {
        _configPath = configPath;
        _ignoreConfigPath = Path.Combine(_configPath, Constants.TrackingIgnoreFileName);
        _includeConfigPath = Path.Combine(_configPath, Constants.TrackingIncludeFileName);
        IOHelper.CreateDirectory(_configPath);
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
        // 숨김 파일 체크
        if (IOHelper.IsHidden(filePath))
        {
            return false;
        }

        // 파일 이름 추출
        string fileName = Path.GetFileName(filePath);

        // 기본 제외 패턴 체크 (파일 이름만 사용)
        if (_defaultExcludePatterns.Any(pattern => IsMatch(fileName, pattern)))
        {
            return false;
        }

        if (_includePatterns.Count > 0)
        {
            if (!_includePatterns.Any(pattern => IsMatch(filePath, pattern)))
            {
                return false;
            }
        }

        return !_ignorePatterns.Any(pattern => IsMatch(filePath, pattern));
    }

    private static bool IsMatch(string input, string pattern)
    {
        // 패턴을 소문자로 변환 (대소문자 구분 없는 매칭)
        pattern = pattern.ToLowerInvariant();

        // 입력을 소문자로 변환
        input = input.ToLowerInvariant();

        // 경로 구분자 정규화
        input = input.Replace('\\', '/');
        pattern = pattern.Replace('\\', '/');

        // 와일드카드를 정규식으로 변환
        string regex = "^" + Regex.Escape(pattern)
                                 .Replace("\\*", ".*")
                                 .Replace("\\?", ".")
                           + "$";

        return Regex.IsMatch(input, regex, RegexOptions.IgnoreCase);
    }

    private static void SavePatterns(string configPath, List<string> patterns)
    {
        // 저장 시 경로 구분자를 플랫폼에 맞게 변환
        var normalizedPatterns = patterns.Select(p => p.Replace('\\', Path.DirectorySeparatorChar)
                                                       .Replace('/', Path.DirectorySeparatorChar))
                                         .ToList();
        File.WriteAllLines(configPath, normalizedPatterns);
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

        // 기본 제외 패턴 추가 (경로 구분자 정규화)
        _ignorePatterns.AddRange(_defaultExcludePatterns.Select(p => p.Replace('\\', '/')));
    }

    public List<string> GetIgnorePatterns() => new(_ignorePatterns);
    public List<string> GetIncludePatterns() => new(_includePatterns);

    public void ClearIgnorePatterns()
    {
        _ignorePatterns.Clear();
        _ignorePatterns.AddRange(_defaultExcludePatterns);  // 기본 제외 패턴은 유지
        SavePatterns(_ignoreConfigPath, _ignorePatterns.Except(_defaultExcludePatterns).ToList());
    }

    public void ClearIncludePatterns()
    {
        _includePatterns.Clear();
        SavePatterns(_includeConfigPath, _includePatterns);
    }

    public bool RemoveIgnorePattern(string pattern)
    {
        bool removed = _ignorePatterns.Remove(pattern);
        if (removed && !_defaultExcludePatterns.Contains(pattern))
        {
            SavePatterns(_ignoreConfigPath, _ignorePatterns.Except(_defaultExcludePatterns).ToList());
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
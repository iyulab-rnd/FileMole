using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GlobExpressions;

namespace FileMoles.Internal;

#if DEBUG // for testing
public
#else
internal
#endif
partial class IgnoreManager : IDisposable
{
    private readonly string _ignoreFilePath;
    private readonly List<PatternEntry> _patterns = [];
    private readonly FileSystemWatcher _watcher;
    protected readonly string _rootDirectory;
    private bool _isInternalChange = false;
    private CancellationTokenSource? _debounceTokenSource;
    private const int DebounceDelay = 500; // milliseconds

    public IgnoreManager(string ignoreFilePath)
    {
        _ignoreFilePath = ignoreFilePath;
        var dir = Path.GetDirectoryName(_ignoreFilePath)!;
        IOHelper.CreateDirectory(dir);

        if (dir.EndsWith(FileMoleGlobalOptions.HillName))
        {
            var dirInfo = new DirectoryInfo(dir);
            _rootDirectory = dirInfo.Parent!.FullName;
        }
        else
        {
            _rootDirectory = dir;
        }

        EnsureIgnoreFileExists();
        LoadPatterns();
        _watcher = SetupWatcher();
    }

    private void EnsureIgnoreFileExists()
    {
        if (!File.Exists(_ignoreFilePath))
        {
            var defaultContent = GetDefaultIgnoreContent();
            File.WriteAllText(_ignoreFilePath, defaultContent);
        }
    }

    protected virtual string GetDefaultIgnoreContent()
    {
        return string.Empty;
    }


    private FileSystemWatcher SetupWatcher()
    {
        var watcher = new FileSystemWatcher(_rootDirectory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            Filter = Path.GetFileName(_ignoreFilePath),
            IncludeSubdirectories = true
        };

        watcher.Created += OnIgnoreFileChanged;
        watcher.Changed += OnIgnoreFileChanged;
        watcher.Deleted += OnIgnoreFileChanged;
        watcher.Renamed += OnIgnoreFileChanged;

        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    protected void LoadPatterns()
    {
        ClearPatterns();
        var directories = GetAllDirectories(_rootDirectory);

        foreach (var dir in directories)
        {
            var ignoreFile = Path.Combine(dir, Path.GetFileName(_ignoreFilePath));
            if (File.Exists(ignoreFile))
            {
                var content = File.ReadAllText(ignoreFile);
                ParseAndAddPatterns(content, dir);
            }
        }
    }

    protected void ClearPatterns()
    {
        _patterns.Clear();
    }

    private void ParseAndAddPatterns(string content, string baseDirectory)
    {
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = RemoveInlineComment(line.Trim());
            if (!string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith('#'))
            {
                AddPattern(trimmedLine, baseDirectory);
            }
        }
    }

    private static string RemoveInlineComment(string line)
    {
        var regex = RegexInlineComment();
        var match = regex.Match(line);
        return match.Success ? match.Groups[1].Value.Trim() : line.Trim();
    }

    private void AddPattern(string pattern, string baseDirectory)
    {
        // # 를 찾아서 왼쪽 텍스트만 가져옴
        pattern = pattern.Contains('#') ? pattern[..pattern.IndexOf('#')] : pattern;
        if (pattern.Length < 1) return;

        bool isInclude = false;
        if (pattern.StartsWith('!'))
        {
            isInclude = true;
            pattern = pattern[1..];
        }

        var patternsToAdd = new List<string>();

        if (pattern.EndsWith('/'))
        {
            var dirPattern = pattern.TrimEnd('/');
            if (!string.IsNullOrEmpty(dirPattern))
            {
                patternsToAdd.Add(dirPattern);
            }
            patternsToAdd.Add(dirPattern + "/**");
        }
        else
        {
            patternsToAdd.Add(pattern);
        }

        foreach (var p in patternsToAdd)
        {
            if (string.IsNullOrWhiteSpace(p))
            {
                continue;
            }

            try
            {
                var compiledPattern = new Glob(p);
                _patterns.Add(new PatternEntry
                {
                    IsInclude = isInclude,
                    PatternText = p,
                    CompiledPattern = compiledPattern,
                    BaseDirectory = baseDirectory
                });
            }
            catch (GlobPatternException)
            {
                // Skip invalid patterns
            }
        }
    }

    private static IEnumerable<string> GetAllDirectories(string rootDirectory)
    {
        var directories = Directory.GetDirectories(rootDirectory, "*", SearchOption.AllDirectories);
        return new[] { rootDirectory }.Concat(directories);
    }

    public bool IsIgnored(string filePath)
    {
        var fileDirectory = Path.GetDirectoryName(filePath)!;

        bool? ignored = null;

        foreach (var patternEntry in _patterns)
        {
            if (IsSubPath(patternEntry.BaseDirectory, fileDirectory))
            {
                var patternRelativePath = GetRelativePath(filePath, patternEntry.BaseDirectory);

                try
                {
                    if (patternEntry.CompiledPattern.IsMatch(patternRelativePath))
                    {
                        ignored = !patternEntry.IsInclude;
                    }
                }
                catch (GlobPatternException)
                {
                    // 패턴이 유효하지 않을 경우 무시
                }
            }
        }

        return ignored ?? false;
    }

    private static bool IsSubPath(string baseDir, string fileDir)
    {
        var baseFullPath = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fileFullPath = Path.GetFullPath(fileDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        return fileFullPath.StartsWith(baseFullPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRelativePath(string filePath, string? baseDir)
    {
        var relativePath = Path.GetRelativePath(baseDir ?? string.Empty, filePath);
        return relativePath.Replace("\\", "/");
    }

    public IEnumerable<string> GetRules()
    {
        return File.ReadAllLines(_ignoreFilePath)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'));
    }

    public void AddRules(string rules)
    {
        if (string.IsNullOrWhiteSpace(rules))
        {
            throw new ArgumentException("Rules cannot be empty or whitespace.", nameof(rules));
        }

        _isInternalChange = true;
        try
        {
            var existingContent = File.ReadAllLines(_ignoreFilePath).ToList();
            var newRules = rules.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
                                .Select(rule => rule.Trim())
                                .Where(rule => !string.IsNullOrWhiteSpace(rule));

            existingContent.AddRange(newRules);
            File.WriteAllLines(_ignoreFilePath, existingContent);

            // Add the new patterns
            foreach (var rule in newRules)
            {
                AddPattern(rule, Path.GetDirectoryName(_ignoreFilePath)!);
            }
        }
        finally
        {
            _isInternalChange = false;
        }
    }

    public void RemoveRules(string rules)
    {
        if (string.IsNullOrWhiteSpace(rules))
        {
            throw new ArgumentException("Rules cannot be empty or whitespace.", nameof(rules));
        }

        _isInternalChange = true;
        try
        {
            var content = File.ReadAllLines(_ignoreFilePath).ToList();
            var rulesToRemove = rules.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
                                     .Select(rule => rule.Trim())
                                     .Where(rule => !string.IsNullOrWhiteSpace(rule))
                                     .ToHashSet();

            content.RemoveAll(line => rulesToRemove.Contains(line.Trim()));
            File.WriteAllLines(_ignoreFilePath, content);

            // Reload all patterns
            LoadPatterns();
        }
        finally
        {
            _isInternalChange = false;
        }
    }

    private void OnIgnoreFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_isInternalChange) return;

        _debounceTokenSource?.Cancel();
        _debounceTokenSource = new CancellationTokenSource();

        Task.Delay(DebounceDelay, _debounceTokenSource.Token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                LoadPatterns();
            }
        }, TaskScheduler.Default);
    }

    [GeneratedRegex(@"^([^#]*)#.*$")]
    private static partial Regex RegexInlineComment();

    public void Dispose()
    {
        _watcher.Dispose();
        GC.SuppressFinalize(this);
    }
}

class PatternEntry
{
    public bool IsInclude { get; set; }
    public required string PatternText { get; set; }
    public required Glob CompiledPattern { get; set; }
    public required string BaseDirectory { get; set; }
}
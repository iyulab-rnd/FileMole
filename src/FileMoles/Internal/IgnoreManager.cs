using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GlobExpressions;

namespace FileMoles.Internal;

#if DEBUG
public
#else
internal
#endif
class IgnoreManager
{
    private readonly string _ignoreFilePath;
    private readonly List<PatternEntry> _patterns = [];

    public IgnoreManager(string ignoreFilePath)
    {
        _ignoreFilePath = ignoreFilePath;
        EnsureIgnoreFileExists();
        LoadPatterns();
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

    private void LoadPatterns()
    {
        var rootDirectory = Path.GetDirectoryName(_ignoreFilePath)!;

        var directories = GetAllDirectories(rootDirectory);

        foreach (var dir in directories)
        {
            var ignoreFile = Path.Combine(dir, Path.GetFileName(_ignoreFilePath));
            if (File.Exists(ignoreFile))
            {
                var content = File.ReadAllText(ignoreFile);
                var lines = content
                    .Split(['\r', '\n' ], StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'));

                foreach (var line in lines)
                {
                    var pattern = line;

                    bool isInclude = false;
                    if (pattern.StartsWith('!'))
                    {
                        isInclude = true;
                        pattern = pattern[1..];
                    }

                    var patternsToAdd = new List<string>();

                    if (pattern.EndsWith('/'))
                    {
                        // Remove trailing slash
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
                                BaseDirectory = dir
                            });
                        }
                        catch (GlobPatternException)
                        {
                            // Skip invalid patterns
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<string> GetAllDirectories(string rootDirectory)
    {
        // Get all directories from the root directory to the leaf directories
        var directories = Directory.GetDirectories(rootDirectory, "*", SearchOption.AllDirectories);
        // Include the root directory itself
        return new[] { rootDirectory }.Concat(directories);
    }

    public bool IsIgnored(string filePath)
    {
        var fileDirectory = Path.GetDirectoryName(filePath)!;

        bool ignored = false;

        foreach (var patternEntry in _patterns)
        {
            // Check if patternEntry.BaseDirectory is equal to or a parent of fileDirectory
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
                    // Skip patterns that cause exceptions during matching
                }
            }
        }

        return ignored;
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
}

class PatternEntry
{
    public bool IsInclude { get; set; }
    public required string PatternText { get; set; }
    public required Glob CompiledPattern { get; set; }
    public required string BaseDirectory { get; set; }
}

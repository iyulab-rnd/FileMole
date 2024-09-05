using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using FileMoles.Diff;
using System.Runtime.InteropServices;
using FileMoles.Utils;
using NPOI.OpenXmlFormats.Dml;

namespace FileMoles;

internal class FileMoleTrack : IDisposable
{
    private readonly string _path;
    private readonly string _molePath;
    private readonly string _ignorePath;
    private readonly string _includePath;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
    private readonly HashSet<string> _trackedFiles = [];
    private readonly List<string> _ignorePatterns = [];
    private readonly List<string> _includePatterns = [];
    private bool _disposed = false;

    public bool HasTrackedFiles => _trackedFiles.Count > 0;

    public FileMoleTrack(string path)
    {
        _path = path;
        _molePath = Path.Combine(path, ".mole");
        _ignorePath = Path.Combine(_molePath, ".ignore");
        _includePath = Path.Combine(_molePath, ".include");
        EnsureDirectoryExists(_molePath);
        LoadIgnoreFile();
        LoadIncludeFile();
    }

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            try
            {
                Directory.CreateDirectory(path);
                SetHiddenAttribute(path);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create directory: {path}", ex);
            }
        }
    }

    private static void SetHiddenAttribute(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
            }
        }
        // Unix-based systems use a dot prefix for hidden files/directories
        // No additional action needed as the file/directory name already starts with a dot
    }

    private void LoadIgnoreFile()
    {
        if (File.Exists(_ignorePath))
        {
            _ignorePatterns.AddRange(File.ReadAllLines(_ignorePath).Where(line => !string.IsNullOrWhiteSpace(line)));
        }
        else
        {
            SetHiddenAttribute(_ignorePath);
        }
    }

    private void LoadIncludeFile()
    {
        if (File.Exists(_includePath))
        {
            _includePatterns.AddRange(File.ReadAllLines(_includePath).Where(line => !string.IsNullOrWhiteSpace(line)));
        }
        else
        {
            SetHiddenAttribute(_includePath);
        }
    }

    public void AddIgnorePattern(string pattern)
    {
        _ignorePatterns.Add(pattern);
        FileSafe.WriteAllTextWithRetry(_ignorePath, string.Join(Environment.NewLine, _ignorePatterns));
    }

    public void AddIncludePattern(string pattern)
    {
        _includePatterns.Add(pattern);
        FileSafe.WriteAllTextWithRetry(_includePath, string.Join(Environment.NewLine, _includePatterns));
    }

    public void ClearIgnorePatterns()
    {
        _ignorePatterns.Clear();
        FileSafe.WriteAllTextWithRetry(_ignorePath, string.Empty);
    }

    public void ClearIncludePatterns()
    {
        _includePatterns.Clear();
        FileSafe.WriteAllTextWithRetry(_includePath, string.Empty);
    }

    public bool RemoveIgnorePattern(string pattern)
    {
        bool removed = _ignorePatterns.Remove(pattern);
        if (removed)
        {
            FileSafe.WriteAllTextWithRetry(_ignorePath, string.Join(Environment.NewLine, _ignorePatterns));
        }
        return removed;
    }

    public bool IsIgnored(string filePath)
    {
        var relativePath = Path.GetRelativePath(_path, filePath);
        return _ignorePatterns.Any(pattern => Regex.IsMatch(relativePath, WildcardToRegex(pattern)));
    }

    public bool IsIncluded(string filePath)
    {
        var relativePath = Path.GetRelativePath(_path, filePath);
        return _includePatterns.Any(pattern => Regex.IsMatch(relativePath, WildcardToRegex(pattern)));
    }

    private static string WildcardToRegex(string pattern)
    {
        return "^" + Regex.Escape(pattern)
                          .Replace("\\*", ".*")
                          .Replace("\\?", ".") + "$";
    }

    public void AddTrackedFile(string filePath)
    {
        if (ShouldTrackFile(filePath))
        {
            _trackedFiles.Add(filePath);
        }
    }

    public void RemoveTrackedFile(string filePath)
    {
        _trackedFiles.Remove(filePath);
    }

    public bool IsTracked(string filePath)
    {
        return (_trackedFiles.Count == 0 || _trackedFiles.Contains(filePath)) && ShouldTrackFile(filePath);
    }

    public bool ShouldTrackFile(string filePath)
    {
        // If there are no include patterns, track all files except those explicitly ignored
        if (_includePatterns.Count == 0)
        {
            return !IsIgnored(filePath);
        }

        // If there are include patterns, only track files that match these patterns
        // and are not explicitly ignored
        return IsIncluded(filePath) && !IsIgnored(filePath);
    }

    public async Task TrackFileAsync(string filePath)
    {
        if (!ShouldTrackFile(filePath))
        {
            return;
        }

        var relativePath = Path.GetRelativePath(_path, filePath);
        var destinationPath = Path.Combine(_molePath, relativePath);
        var semaphore = _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            EnsureDirectoryExists(Path.GetDirectoryName(destinationPath)!);
            await FileMoleUtils.CopyFileAsync(filePath, destinationPath);
            Console.WriteLine($"Tracked file: {filePath}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error tracking file: {filePath}. Error: {ex.Message}");
            throw new InvalidOperationException($"Failed to track file: {filePath}", ex);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<DiffResult?> TrackAndGetDiffAsync(string filePath)
    {
        if (!ShouldTrackFile(filePath))
        {
            return null;
        }

        var relativePath = Path.GetRelativePath(_path, filePath);
        var destinationPath = Path.Combine(_molePath, relativePath);
        var semaphore = _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            EnsureDirectoryExists(Path.GetDirectoryName(destinationPath)!);
            DiffResult diff;

            if (File.Exists(destinationPath))
            {
                var diffStrategy = DiffStrategyFactory.CreateStrategy(filePath);
                diff = await diffStrategy.GenerateDiffAsync(destinationPath, filePath);
            }
            else
            {
                // 파일이 처음 추적될 때
                diff = new TextDiffResult
                {
                    FileType = "Text",
                    Entries =
                    [
                        new TextDiffEntry
                        {
                            Type = DiffType.Inserted,
                            ModifiedText = await File.ReadAllTextAsync(filePath)
                        }
                    ],
                    IsInitial = true  // 초기 버전임을 나타내는 플래그 추가
                };
            }

            // 백업 파일 생성
            await FileMoleUtils.CopyFileAsync(filePath, destinationPath);
            Console.WriteLine($"Tracked and compared file: {filePath}.");

            return diff;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error tracking file: {filePath}. Error: {ex.Message}");
            throw new InvalidOperationException($"Failed to track and compare file: {filePath}", ex);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task RemoveFileAsync(string filePath)
    {
        if (!ShouldTrackFile(filePath))
        {
            return;
        }

        var relativePath = Path.GetRelativePath(_path, filePath);
        var trackedPath = Path.Combine(_molePath, relativePath);
        var semaphore = _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            if (File.Exists(trackedPath))
            {
                File.Delete(trackedPath);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to remove tracked file: {filePath}", ex);
        }
        finally
        {
            semaphore.Release();
        }

        RemoveTrackedFile(filePath);
    }

    public List<string> GetIgnorePatterns()
    {
        return new List<string>(_ignorePatterns);
    }

    public List<string> GetIncludePatterns()
    {
        return new List<string>(_includePatterns);
    }

    public async Task<List<string>> GetTrackedFilesAsync()
    {
        List<string> trackedFiles = [];

        await Task.Run(() =>
        {
            foreach (var file in Directory.EnumerateFiles(_path, "*", SearchOption.AllDirectories))
            {
                if (ShouldTrackFile(file))
                {
                    trackedFiles.Add(file);
                }
            }
        });

        return trackedFiles;
    }

    public async Task RefreshTrackedFilesAsync()
    {
        _trackedFiles.Clear();
        var files = await GetTrackedFilesAsync();
        foreach (var file in files)
        {
            _trackedFiles.Add(file);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                foreach (var semaphore in _fileLocks.Values)
                {
                    semaphore.Dispose();
                }
                _fileLocks.Clear();
            }
            _disposed = true;
        }
    }
}
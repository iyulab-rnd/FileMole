using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using FileMoles.Diff;

namespace FileMoles
{
    public class FileMoleTrack : IDisposable
    {
        private readonly string _path;
        private readonly string _molePath;
        private readonly string _ignorePath;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();
        private readonly HashSet<string> _trackedFiles = new();
        private readonly List<string> _ignorePatterns = new();
        private bool _disposed = false;
        private readonly FileMoleOptions _options;

        public bool HasTrackedFiles => _trackedFiles.Count > 0;

        public FileMoleTrack(string path, FileMoleOptions options)
        {
            _path = path;
            _options = options;
            _molePath = Path.Combine(path, ".mole");
            _ignorePath = Path.Combine(_molePath, ".ignore");
            EnsureDirectoryExists(_molePath);
            LoadIgnoreFile();
        }

        private void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to create directory: {path}", ex);
                }
            }
        }

        private void LoadIgnoreFile()
        {
            if (File.Exists(_ignorePath))
            {
                _ignorePatterns.AddRange(File.ReadAllLines(_ignorePath));
            }
        }

        public void AddIgnorePattern(string pattern)
        {
            _ignorePatterns.Add(pattern);
            File.AppendAllLines(_ignorePath, new[] { pattern });
        }

        public bool IsIgnored(string filePath)
        {
            var relativePath = Path.GetRelativePath(_path, filePath);
            return _ignorePatterns.Any(pattern => Regex.IsMatch(relativePath, WildcardToRegex(pattern)));
        }

        private static string WildcardToRegex(string pattern)
        {
            return "^" + Regex.Escape(pattern)
                              .Replace("\\*", ".*")
                              .Replace("\\?", ".") + "$";
        }

        public void AddTrackedFile(string filePath)
        {
            if (!IsIgnored(filePath))
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
            return (_trackedFiles.Contains(filePath) || _trackedFiles.Count == 0) && !IsIgnored(filePath);
        }

        public async Task TrackFileAsync(string filePath)
        {
            if (IsIgnored(filePath))
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
            if (IsIgnored(filePath))
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
                    diff = new TextDiffResult
                    {
                        FileType = "Text",
                        Entries = new List<TextDiffEntry>
                        {
                            new TextDiffEntry
                            {
                                Type = DiffType.Inserted,
                                ModifiedText = "Initial version created"
                            }
                        }
                    };
                }

                // Backup the file
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
            if (IsIgnored(filePath))
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

        public bool ShouldTrackFile(string filePath)
        {
            return !IsIgnored(filePath) && (_trackedFiles.Count == 0 || _trackedFiles.Contains(filePath));
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
}
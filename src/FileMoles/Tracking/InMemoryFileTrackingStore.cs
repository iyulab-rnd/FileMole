using System.Collections.Concurrent;
using FileMoles.Data;

namespace FileMoles.Tracking;

/// <summary>
/// In-memory, responsive, and DB I/O is done asynchronously.
/// </summary>
internal class InMemoryFileTrackingStore : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, TrackingFile> _trackingFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly IUnitOfWork _unitOfWork;
    private readonly ConcurrentQueue<Func<Task>> _databaseOperations = new();
    private readonly Task _databaseWorker;
    private readonly CancellationTokenSource _cts = new();

    public InMemoryFileTrackingStore(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
        _databaseWorker = Task.Run(ProcessDatabaseOperationsAsync);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var allTrackingFiles = await _unitOfWork.TrackingFiles.GetAllAsync();
        foreach (var file in allTrackingFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _trackingFiles[file.FullPath] = file;
        }
    }

    public bool TryGetTrackingFile(string fullPath, out TrackingFile? trackingFile)
    {
        return _trackingFiles.TryGetValue(fullPath, out trackingFile);
    }

    public void AddOrUpdateTrackingFile(TrackingFile trackingFile)
    {
        _trackingFiles[trackingFile.FullPath] = trackingFile;
        EnqueueDatabaseOperation(async () =>
        {
            await _unitOfWork.TrackingFiles.UpsertAsync(trackingFile);
        });
    }

    public void RemoveTrackingFile(string fullPath)
    {
        if (_trackingFiles.TryRemove(fullPath, out var removedFile))
        {
            EnqueueDatabaseOperation(async () =>
            {
                await _unitOfWork.TrackingFiles.DeleteAsync(removedFile);
            });
        }
    }
    public bool IsTrackingFile(string fullPath) => _trackingFiles.ContainsKey(fullPath);

    public IEnumerable<TrackingFile> GetAllTrackingFiles() => _trackingFiles.Values;

    public IEnumerable<string> GetTrackedFilesInDirectory(string directoryPath)
    {
        return _trackingFiles.Values
            .Where(tf => tf.FullPath.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase))
            .Select(tf => tf.FullPath);
    }
    private void EnqueueDatabaseOperation(Func<Task> operation)
    {
        if (!_cts.IsCancellationRequested)
        {
            _databaseOperations.Enqueue(operation);
        }
    }

    private async Task ProcessDatabaseOperationsAsync()
    {
        while (!_cts.Token.IsCancellationRequested || !_databaseOperations.IsEmpty)
        {
            if (_databaseOperations.TryDequeue(out var operation))
            {
                try
                {
                    await operation();
                }
                catch (Exception ex)
                {
                    // Log the error, but continue processing
                    Console.WriteLine($"Error processing database operation: {ex.Message}");
                }
            }
            else
            {
                await Task.Delay(100); // Wait a bit before checking again
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await _databaseWorker;
        _cts.Dispose();
    }
}
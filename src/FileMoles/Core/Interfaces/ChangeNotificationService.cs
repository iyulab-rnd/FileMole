using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace FileMoles.Core.Interfaces;

public class ChangeNotificationService : IChangeNotificationService
{
    private readonly ILogger<ChangeNotificationService> _logger;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<FileSystemChange>> _pendingChanges;
    private readonly IOptions<MonitoringOptions> _options;

    public event EventHandler<FileSystemChangeEventArgs> ChangesAvailable;

    public ChangeNotificationService(
        ILogger<ChangeNotificationService> logger,
        IOptions<MonitoringOptions> options)
    {
        _logger = logger;
        _options = options;
        _pendingChanges = new ConcurrentDictionary<string, ConcurrentQueue<FileSystemChange>>();
    }

    public async Task NotifyChangesAsync(string providerId, IEnumerable<FileSystemChange> changes)
    {
        var queue = _pendingChanges.GetOrAdd(providerId, _ => new ConcurrentQueue<FileSystemChange>());

        foreach (var change in changes)
        {
            change.Id = Guid.NewGuid().ToString();
            change.Timestamp = DateTime.UtcNow;
            queue.Enqueue(change);

            _logger.LogDebug(
                "New change notification: {ChangeType} on {Path} for {ProviderId}",
                change.ChangeType,
                change.Path,
                providerId);
        }

        // Raise event for subscribers
        OnChangesAvailable(new FileSystemChangeEventArgs(providerId, changes));

        // Clean up old changes if needed
        await CleanupOldChangesAsync(providerId);
    }

    public Task<IEnumerable<FileSystemChange>> GetPendingChangesAsync(string providerId)
    {
        if (!_pendingChanges.TryGetValue(providerId, out var queue))
            return Task.FromResult(Enumerable.Empty<FileSystemChange>());

        return Task.FromResult(queue.ToArray().AsEnumerable());
    }

    public Task AcknowledgeChangesAsync(string providerId, IEnumerable<string> changeIds)
    {
        if (!_pendingChanges.TryGetValue(providerId, out var queue))
            return Task.CompletedTask;

        var remainingChanges = queue
            .Where(c => !changeIds.Contains(c.Id))
            .ToArray();

        var newQueue = new ConcurrentQueue<FileSystemChange>(remainingChanges);
        _pendingChanges.TryUpdate(providerId, newQueue, queue);

        return Task.CompletedTask;
    }

    private async Task CleanupOldChangesAsync(string providerId)
    {
        if (!_pendingChanges.TryGetValue(providerId, out var queue))
            return;

        var expirationTime = DateTime.UtcNow - _options.Value.ChangeRetentionPeriod;
        var validChanges = queue
            .Where(c => c.Timestamp > expirationTime)
            .ToArray();

        var newQueue = new ConcurrentQueue<FileSystemChange>(validChanges);
        _pendingChanges.TryUpdate(providerId, newQueue, queue);
    }

    protected virtual void OnChangesAvailable(FileSystemChangeEventArgs e)
    {
        ChangesAvailable?.Invoke(this, e);
    }
}

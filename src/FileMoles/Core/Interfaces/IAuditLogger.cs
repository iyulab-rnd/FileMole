using FileMoles.Security;

namespace FileMoles.Core.Interfaces;

public interface IAuditLogger
{
    Task LogAccessAsync(FileSystemAuditEntry entry);
    Task<IEnumerable<FileSystemAuditEntry>> GetEntriesAsync(
        string providerId = null,
        DateTime? startTime = null,
        DateTime? endTime = null);
    Task<IEnumerable<FileSystemAuditEntry>> GetUserEntriesAsync(
        string username,
        DateTime? startTime = null,
        DateTime? endTime = null);
    Task ClearEntriesAsync(DateTime before);
}
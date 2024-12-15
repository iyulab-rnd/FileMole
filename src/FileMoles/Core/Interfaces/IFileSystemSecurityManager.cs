using FileMoles.Core.Models;
using FileMoles.Security;

namespace FileMoles.Core.Interfaces;

public interface IFileSystemSecurityManager
{
    Task<bool> ValidateAccessAsync(string providerId, string path, FileAccessRights requiredRights);
    Task<Stream> EncryptStreamAsync(Stream input, string key);
    Task<Stream> DecryptStreamAsync(Stream input, string key);
    Task<FileSecurityInfo> GetSecurityInfoAsync(string providerId, string path);
    Task SetSecurityInfoAsync(string providerId, string path, FileSecurityInfo securityInfo);
    void AuditAccess(string providerId, string path, string operation, bool success);
    Task<IEnumerable<FileSystemAuditEntry>> GetAuditLogAsync(
        string providerId,
        DateTime? startTime = null,
        DateTime? endTime = null);
}

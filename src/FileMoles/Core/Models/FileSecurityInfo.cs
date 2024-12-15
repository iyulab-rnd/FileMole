namespace FileMoles.Core.Models;

public class FileSecurityInfo
{
    public string Owner { get; set; }
    public string Group { get; set; }
    public FileAccessRights AccessRights { get; set; }
}

namespace FileMoles.Core.Models;

[Flags]
public enum FileAccessRights
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
    FullControl = Read | Write | Execute
}
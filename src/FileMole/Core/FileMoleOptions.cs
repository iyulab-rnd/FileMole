using FileMole.Storage;
using FileMole.Events;
using FileMole.Indexing;
using System;

namespace FileMole.Core;

public class FileMoleOptions
{
    public IStorageProvider StorageProvider { get; set; }
    public IFMFileSystemWatcher FileSystemWatcher { get; set; }
    public IFileIndexer FileIndexer { get; set; }
    public TimeSpan DebouncePeriod { get; set; } = TimeSpan.FromMilliseconds(300);
    public string DatabasePath { get; set; }
}
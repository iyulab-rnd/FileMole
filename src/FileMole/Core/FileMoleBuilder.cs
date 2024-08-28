using FileMole.Storage;
using FileMole.Events;
using FileMole.Indexing;
using System;
using System.IO;

namespace FileMole.Core;

public class FileMoleBuilder
{
    private FileMoleOptions _options = new FileMoleOptions();

    public FileMoleBuilder UseLocalStorage()
    {
        _options.StorageProvider = new LocalStorageProvider();
        return this;
    }

    public FileMoleBuilder UseFileSystemWatcher()
    {
        _options.FileSystemWatcher = new FMFileSystemWatcher(_options.DebouncePeriod);
        return this;
    }

    public FileMoleBuilder UseFileIndexer()
    {
        _options.FileIndexer = new FileIndexer(_options);
        return this;
    }

    public FileMoleBuilder WithDebouncePeriod(TimeSpan period)
    {
        _options.DebouncePeriod = period;
        return this;
    }

    public FileMoleBuilder UseDefaultDatabasePath()
    {
        string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string fileMolePath = Path.Combine(localAppDataPath, "FileMole");
        Directory.CreateDirectory(fileMolePath);  // 디렉토리가 없으면 생성
        _options.DatabasePath = Path.Combine(fileMolePath, "filemole.db");
        return this;
    }

    public FileMoleBuilder UseDatabasePath(string path)
    {
        _options.DatabasePath = path;
        return this;
    }

    public IFileMole Build()
    {
        return new FileMole(_options);
    }
}
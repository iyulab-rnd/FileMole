using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileMole.Indexing;
using FileMole.Storage;
using FileMole.Core;
using FileMole.Utils;
using Xunit;

namespace FileMole.Tests.Indexing;

public class FileIndexerTests : IAsyncLifetime
{
    private FileIndexer _indexer;
    private readonly string _testDirectory;
    private readonly string _dbPath;

    public FileIndexerTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "FileMoleIndexerTests_" + Guid.NewGuid().ToString());
        _dbPath = Path.Combine(_testDirectory, "test.db");

        Directory.CreateDirectory(_testDirectory);
        PrepareTestFiles();
    }

    private void PrepareTestFiles()
    {
        File.WriteAllText(Path.Combine(_testDirectory, "file1.txt"), "This is file 1");
        File.WriteAllText(Path.Combine(_testDirectory, "file2.txt"), "This is file 2");
        File.WriteAllText(Path.Combine(_testDirectory, "file3.txt"), "This is file 3");
    }

    public async Task InitializeAsync()
    {
        var options = new FileMoleOptions
        {
            DatabasePath = _dbPath
        };
        _indexer = new FileIndexer(options);
        await _indexer.ClearDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        await _indexer.DisposeAsync();

        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }

            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Warning: Unable to delete test directory or database: {ex.Message}");
        }
    }

    [Fact]
    public async Task IndexFileAsync_ShouldIndexFile()
    {
        var filePath = Path.Combine(_testDirectory, "file1.txt");
        var fileInfo = new FileInfo(filePath);
        var file = FMFileInfo.FromFileInfo(fileInfo);

        await _indexer.IndexFileAsync(file);

        var result = await _indexer.SearchAsync("file1.txt");
        Assert.Single(result);
        Assert.Equal(file.Name, result.First().Name);
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnCorrectResults()
    {
        var file1 = FMFileInfo.FromFileInfo(new FileInfo(Path.Combine(_testDirectory, "file1.txt")));
        var file2 = FMFileInfo.FromFileInfo(new FileInfo(Path.Combine(_testDirectory, "file2.txt")));
        await _indexer.IndexFileAsync(file1);
        await _indexer.IndexFileAsync(file2);

        var result1 = await _indexer.SearchAsync("file1");
        var result2 = await _indexer.SearchAsync("txt");

        Assert.Single(result1);
        Assert.Equal(file1.Name, result1.First().Name);
        Assert.Equal(2, result2.Count());
        Assert.Contains(result2, f => f.Name == file1.Name);
        Assert.Contains(result2, f => f.Name == file2.Name);
    }

    [Fact]
    public async Task IndexFileAsync_ShouldUpdateExistingFile()
    {
        var filePath = Path.Combine(_testDirectory, "updatefile.txt");
        File.WriteAllText(filePath, "Initial content");
        var initialFile = FMFileInfo.FromFileInfo(new FileInfo(filePath));
        await _indexer.IndexFileAsync(initialFile);

        var initialFileInfo = await _indexer.GetFileInfoAsync(filePath);
        var initialHash = initialFileInfo.FileHash;

        await Task.Delay(1000); // 파일 수정 시간이 확실히 변경되도록 대기

        File.WriteAllText(filePath, "Updated content with more text");
        var updatedFile = FMFileInfo.FromFileInfo(new FileInfo(filePath));
        await _indexer.IndexFileAsync(updatedFile);

        var updatedFileInfo = await _indexer.GetFileInfoAsync(filePath);
        var updatedHash = updatedFileInfo.FileHash;

        Assert.NotEqual(initialFile.Size, updatedFileInfo.Size);
        Assert.NotEqual(initialHash, updatedHash);
        Assert.NotEqual(initialFile.LastWriteTime, updatedFileInfo.LastWriteTime);
    }

    [Fact]
    public async Task HasFileChangedAsync_ShouldDetectChanges()
    {
        var filePath = Path.Combine(_testDirectory, "changingfile.txt");
        File.WriteAllText(filePath, "Initial content");
        var initialFile = FMFileInfo.FromFileInfo(new FileInfo(filePath));
        await _indexer.IndexFileAsync(initialFile);

        Assert.False(await _indexer.HasFileChangedAsync(initialFile));

        await Task.Delay(1000); // 파일 수정 시간이 확실히 변경되도록 대기

        File.WriteAllText(filePath, "Modified content");
        var modifiedFile = FMFileInfo.FromFileInfo(new FileInfo(filePath));

        Assert.True(await _indexer.HasFileChangedAsync(modifiedFile));
    }

    [Fact]
    public async Task RemoveFileAsync_ShouldRemoveFileFromIndex()
    {
        var filePath = Path.Combine(_testDirectory, "file1.txt");
        var file = FMFileInfo.FromFileInfo(new FileInfo(filePath));
        await _indexer.IndexFileAsync(file);

        await _indexer.RemoveFileAsync(file.FullPath);

        var result = await _indexer.SearchAsync("file1.txt");
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetFileCountAsync_ShouldReturnCorrectCount()
    {
        // 테스트 디렉터리의 파일 정보를 가져와 FMFileInfo 객체로 변환
        var files = Directory.GetFiles(_testDirectory)
                             .Select(f => FMFileInfo.FromFileInfo(new FileInfo(f)))
                             .ToArray();

        // 모든 파일을 인덱싱
        foreach (var file in files)
        {
            await _indexer.IndexFileAsync(file);
        }

        // 테스트 디렉터리의 파일 개수 조회
        var result = await _indexer.GetFileCountAsync(_testDirectory);

        // 예상되는 파일 개수와 실제 결과를 비교
        Assert.Equal(files.Length, result);
    }

    [Fact]
    public async Task ClearDatabaseAsync_ShouldRemoveAllEntries()
    {
        var files = Directory.GetFiles(_testDirectory)
                             .Select(f => FMFileInfo.FromFileInfo(new FileInfo(f)))
                             .ToArray();

        foreach (var file in files)
        {
            await _indexer.IndexFileAsync(file);
        }

        await _indexer.ClearDatabaseAsync();

        var result = await _indexer.SearchAsync("file");
        Assert.Empty(result);
    }
}
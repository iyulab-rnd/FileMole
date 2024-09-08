using FileMoles.Storage;
using System.Collections.Generic;

namespace FileMoles.Tests.Storage;

public class LocalStorageProviderTests : IDisposable
{
    private readonly LocalStorageProvider _provider;
    private readonly string _testDirectory;

    public LocalStorageProviderTests()
    {
        _provider = new LocalStorageProvider();
        _testDirectory = Path.Combine(Path.GetTempPath(), "FileMoleTests");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        Directory.Delete(_testDirectory, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetFilesAsync_ShouldReturnCorrectFiles()
    {
        // Arrange
        var file1 = Path.Combine(_testDirectory, "file1.txt");
        var file2 = Path.Combine(_testDirectory, "file2.txt");
        File.WriteAllText(file1, "Test content");
        File.WriteAllText(file2, "Test content");

        // Act
        var files = new List<FileInfo>();
        await foreach (var file in _provider.GetFilesAsync(_testDirectory))
        {
            files.Add(file);
        }

        // Assert
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Name == "file1.txt");
        Assert.Contains(files, f => f.Name == "file2.txt");
    }

    [Fact]
    public async Task GetDirectoriesAsync_ShouldReturnCorrectDirectories()
    {
        // Arrange
        var dir1 = Path.Combine(_testDirectory, "dir1");
        var dir2 = Path.Combine(_testDirectory, "dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        // Act
        var directories = new List<DirectoryInfo>();
        await foreach (var directory in _provider.GetDirectoriesAsync(_testDirectory))
        {
            directories.Add(directory);
        }

        // Assert
        Assert.Equal(2, directories.Count);
        Assert.Contains(directories, d => d.Name == "dir1");
        Assert.Contains(directories, d => d.Name == "dir2");
    }

    [Fact]
    public async Task CreateDirectoryAsync_ShouldCreateDirectory()
    {
        // Arrange
        var newDir = Path.Combine(_testDirectory, "newDir");

        // Act
        await _provider.CreateDirectoryAsync(newDir);

        // Assert
        Assert.True(Directory.Exists(newDir));
    }

    [Fact]
    public async Task DeleteFileAsync_ShouldDeleteFile()
    {
        // Arrange
        var file = Path.Combine(_testDirectory, "fileToDelete.txt");
        File.WriteAllText(file, "Test content");

        // Act
        await _provider.DeleteFileAsync(file);

        // Assert
        Assert.False(File.Exists(file));
    }
}
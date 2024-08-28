using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileMole.Indexing;
using FileMole.Storage;
using FileMole.Core;
using Xunit;

namespace FileMole.Tests.Indexing
{
    public class FileIndexerTests : IDisposable
    {
        private readonly FileIndexer _indexer;
        private readonly string _testDirectory;
        private readonly string _dbPath;

        public FileIndexerTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "FileMoleIndexerTests_" + Guid.NewGuid().ToString());
            _dbPath = Path.Combine(_testDirectory, "test_" + Guid.NewGuid().ToString() + ".db");

            Directory.CreateDirectory(_testDirectory);

            var options = new FileMoleOptions { DatabasePath = _dbPath };
            _indexer = new FileIndexer(options);
        }

        public void Dispose()
        {
            _indexer.Dispose();

            // 데이터베이스 연결이 완전히 닫힐 때까지 잠시 대기
            System.Threading.Thread.Sleep(500);

            try
            {
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, true);
                }
            }
            catch (IOException)
            {
                // 파일이 여전히 사용 중이라면 무시
                Console.WriteLine($"Warning: Unable to delete test directory: {_testDirectory}");
            }

            GC.SuppressFinalize(this);
        }

        [Fact]
        public async Task IndexFileAsync_ShouldIndexFile()
        {
            await _indexer.ClearDatabaseAsync();

            // Arrange
            var file = new FMFileInfo("testfile.txt", Path.Combine(_testDirectory, "testfile.txt"), 100, DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal);

            // Act
            await _indexer.IndexFileAsync(file);

            // Assert
            var result = await _indexer.SearchAsync("testfile.txt");
            Assert.Single(result);
            Assert.Equal(file.Name, result.First().Name);
        }

        [Fact]
        public async Task SearchAsync_ShouldReturnCorrectResults()
        {
            await _indexer.ClearDatabaseAsync();

            // Arrange
            var file1 = new FMFileInfo("file1.txt", Path.Combine(_testDirectory, "file1.txt"), 100, DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal);
            var file2 = new FMFileInfo("file2.txt", Path.Combine(_testDirectory, "file2.txt"), 200, DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal);
            await _indexer.IndexFileAsync(file1);
            await _indexer.IndexFileAsync(file2);

            // Act
            var result1 = await _indexer.SearchAsync("file1");
            var result2 = await _indexer.SearchAsync("txt");

            // Assert
            Assert.Single(result1);
            Assert.Equal(file1.Name, result1.First().Name);
            Assert.Equal(2, result2.Count());
            Assert.Contains(result2, f => f.Name == file1.Name);
            Assert.Contains(result2, f => f.Name == file2.Name);
        }

        [Fact]
        public async Task IndexFileAsync_ShouldUpdateExistingFile()
        {
            await _indexer.ClearDatabaseAsync();

            // Arrange
            var file = new FMFileInfo("updatefile.txt", Path.Combine(_testDirectory, "updatefile.txt"), 100, DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal);
            await _indexer.IndexFileAsync(file);

            // Act
            var updatedFile = new FMFileInfo(file.Name, file.FullPath, 200, file.CreationTime, DateTime.Now, DateTime.Now, FileAttributes.ReadOnly);
            await _indexer.IndexFileAsync(updatedFile);

            // Assert
            var result = await _indexer.SearchAsync("updatefile.txt");
            Assert.Single(result);
            Assert.Equal(200, result.First().Size);
            Assert.Equal(FileAttributes.ReadOnly, result.First().Attributes);
        }

        [Fact]
        public async Task SearchAsync_ShouldReturnEmptyResultForNonExistentFile()
        {
            await _indexer.ClearDatabaseAsync();

            // Act
            var result = await _indexer.SearchAsync("nonexistentfile.txt");

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public async Task IndexFileAsync_ShouldHandleMultipleFiles()
        {
            await _indexer.ClearDatabaseAsync();

            // Arrange
            var files = new[]
            {
                new FMFileInfo("file1.txt", Path.Combine(_testDirectory, "file1.txt"), 100, DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal),
                new FMFileInfo("file2.txt", Path.Combine(_testDirectory, "file2.txt"), 200, DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal),
                new FMFileInfo("file3.txt", Path.Combine(_testDirectory, "file3.txt"), 300, DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal)
            };

            // Act
            foreach (var file in files)
            {
                await _indexer.IndexFileAsync(file);
            }

            // Assert
            var result = await _indexer.SearchAsync("file");
            Assert.Equal(3, result.Count());
            foreach (var file in files)
            {
                Assert.Contains(result, f => f.Name == file.Name && f.Size == file.Size);
            }
        }
    }
}
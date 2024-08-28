using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileMole.Core;
using FileMole.Events;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("FileMole Sample Application");

        var fileMole = new FileMoleBuilder()
            .UseLocalStorage()
            .UseFileSystemWatcher()
            .UseFileIndexer()
            .WithDebouncePeriod(TimeSpan.FromSeconds(1))
            .Build();

        var drives = DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName).ToList();

        foreach (var drive in drives)
        {
            await fileMole.WatchDirectoryAsync(drive);
            Console.WriteLine($"Monitoring drive: {drive}");
        }

        fileMole.FileSystemChanged += (sender, e) =>
        {
            Console.WriteLine($"Event: {e.ChangeType}, Path: {e.FullPath}");
        };

        Console.WriteLine("Indexing files...");
        await fileMole.IndexAllAsync();
        Console.WriteLine("Indexing complete.");

        while (true)
        {
            Console.Write("Enter search term (or 'exit' to quit): ");
            var searchTerm = Console.ReadLine();

            if (searchTerm?.ToLower() == "exit")
                break;

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var searchResults = await fileMole.SearchAsync(searchTerm);
                Console.WriteLine($"Search results for '{searchTerm}':");
                foreach (var result in searchResults)
                {
                    Console.WriteLine($"- {result.FullPath}");
                }
            }
        }

        fileMole.Dispose();
    }
}
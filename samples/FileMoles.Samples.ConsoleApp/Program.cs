using FileMoles;
using FileMoles.Storage;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("FileMole Sample Application");

        var mole1 = new Mole { Path = @"C:\", Type = MoleType.Local, Provider = "Default" };

        var fileMole = new FileMoleBuilder()
            .AddMole(mole1)
            .Build();

        // 파일 시스템 변경 이벤트 구독
        fileMole.InitialScanCompleted += (sender, e) =>
        {
            Console.WriteLine("Initial scan completed.");
        };

        fileMole.FileChanged += (sender, e) =>
        {
            Console.WriteLine($"File changed: {e.FullPath}");
        };

        fileMole.FileCreated += (sender, e) =>
        {
            Console.WriteLine($"File created: {e.FullPath}");
        };

        fileMole.FileDeleted += (sender, e) =>
        {
            Console.WriteLine($"File deleted: {e.FullPath}");
        };

        fileMole.FileRenamed += (sender, e) =>
        {
            Console.WriteLine($"File renamed: {e.OldFullPath} -> {e.FullPath}");
        };

        fileMole.MoleTrackChanged += (sender, e) =>
        {
            Console.WriteLine($"MoleTrackChanged: {e.FullPath}");
        };

        // 파일 검색 예제
        Console.WriteLine("Searching for files containing 'example'...");
        var searchResults = await fileMole.SearchAsync("example");
        foreach (var file in searchResults)
        {
            Console.WriteLine($"Found: {file.FullPath}");
        }

        // 디렉토리 내용 나열 예제
        Console.WriteLine("\nListing directory contents...");
        var localProvider = new LocalStorageProvider();
        var files = await localProvider.GetFilesAsync(mole1.Path);
        var directories = await localProvider.GetDirectoriesAsync(mole1.Path);

        Console.WriteLine("Files:");
        foreach (var file in files)
        {
            Console.WriteLine($"- {file.Name}");
        }

        Console.WriteLine("Directories:");
        foreach (var dir in directories)
        {
            Console.WriteLine($"- {dir.Name}");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}
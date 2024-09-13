using FileMoles.Internal;

namespace FileMoles.Tests;

public class FileIgnoreTests : IDisposable
{
    private string testPath;
    private readonly string _ignoreFilePath;

    public FileIgnoreTests()
    {
        var basePath = @"c:/test";
        this.testPath = Path.Combine(basePath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(testPath);

        if (!Directory.Exists(testPath))
        {
            Directory.CreateDirectory(testPath);
        }
        _ignoreFilePath = Path.Combine(testPath, ".ignore");
        if (File.Exists(_ignoreFilePath))
        {
            File.Delete(_ignoreFilePath);
        }
    }

    public void Dispose()
    {
        Directory.Delete(this.testPath, true);
    }

    [Fact]
    public void ShouldIgnore_DefaultIgnores_ReturnsTrue()
    {
        var manager = new FileIgnoreManager(testPath);

        Assert.True(manager.ShouldIgnore(".gitignore"));
        Assert.True(manager.ShouldIgnore("src/bin/debug/app.exe"));
        Assert.True(manager.ShouldIgnore("src/obj/project.assets.json"));
        Assert.True(manager.ShouldIgnore("packages/newtonsoft.json/13.0.1/lib/net45/Newtonsoft.Json.dll"));
        Assert.True(manager.ShouldIgnore("logs/error.log"));
        Assert.True(manager.ShouldIgnore("data/app.db"));
    }

    [Fact]
    public void ShouldIgnore_NonIgnoredFiles_ReturnsFalse()
    {
        var manager = new FileIgnoreManager(testPath);

        Assert.False(manager.ShouldIgnore("src/Program.cs"));
        Assert.False(manager.ShouldIgnore("docs/README.md"));
        Assert.False(manager.ShouldIgnore("images/logo.png"));
    }

    [Fact]
    public void ShouldIgnore_HiddenFiles_ReturnsTrue()
    {
        var manager = new FileIgnoreManager(testPath);

        Assert.True(manager.ShouldIgnore(".hidden_file"));
        Assert.True(manager.ShouldIgnore(".config/settings.json"));
    }

    [Fact]
    public void ShouldIgnore_NestedIgnoredPaths_ReturnsTrue()
    {
        var manager = new FileIgnoreManager(testPath);

        Assert.True(manager.ShouldIgnore("project1/node_modules/package/index.js"));
        Assert.True(manager.ShouldIgnore("project2/build/output.dll"));
        Assert.False(manager.ShouldIgnore("project2/code.cpp"));
    }

    [Fact]
    public void AddIgnorePattern_NewPattern_ShouldBeIgnored()
    {
        var manager = new FileIgnoreManager(testPath);
        var newPattern = "*.secret";

        manager.AddIgnorePattern(newPattern);

        Assert.True(manager.ShouldIgnore("config.secret"));
        Assert.True(manager.ShouldIgnore("passwords.secret"));
    }

    [Fact]
    public void ShouldIgnore_CaseInsensitivity_WorksCorrectly()
    {
        var manager = new FileIgnoreManager(testPath);

        Assert.True(manager.ShouldIgnore("LOGS/ERROR.LOG"));
        Assert.True(manager.ShouldIgnore("packages/NewtonSoft.Json/13.0.1/lib/net45/newtonsoft.json.dll"));
    }

    [Fact]
    public void ShouldIgnore_WildcardPatterns_WorkCorrectly()
    {
        var manager = new FileIgnoreManager(testPath);

        Assert.True(manager.ShouldIgnore("temp_file.tmp"));
        Assert.True(manager.ShouldIgnore("backup.bak"));
        Assert.True(manager.ShouldIgnore("project.swp"));
    }

    [Fact]
    public void ShouldIgnore_IgnoreFileUpdated_ReflectsChanges()
    {
        var manager = new FileIgnoreManager(testPath);
        Assert.False(manager.ShouldIgnore(Path.Combine(testPath, "data.csv")));

        File.AppendAllText(_ignoreFilePath, Environment.NewLine + "*.csv");

        // 새로운 FileIgnoreManager 인스턴스를 생성하여 변경사항 반영
        manager = new FileIgnoreManager(testPath);
        Assert.True(manager.ShouldIgnore(Path.Combine(testPath, "data.csv")));
    }

    [Fact]
    public void ShouldIgnore_RelativePathHandling_WorksCorrectly()
    {
        var manager = new FileIgnoreManager(testPath);

        Assert.False(manager.ShouldIgnore(Path.Combine(testPath, "..", "data.csv")));

        File.AppendAllText(_ignoreFilePath, Environment.NewLine + "*.csv");

        manager = new FileIgnoreManager(testPath);
        Assert.True(manager.ShouldIgnore(Path.Combine(testPath, "..", "data.csv")));
    }
}
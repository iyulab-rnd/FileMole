using System;
using System.IO;
using System.Linq;
using FileMoles.Tracking;
using Xunit;

namespace FileMoles.Tests;

public class TrackingIgnoreManagerTests : IDisposable
{
    private readonly string testPath;
    private readonly string ignoreFilePath;

    public TrackingIgnoreManagerTests()
    {
        var tempPath = Path.GetTempPath();
        this.testPath = Path.Combine(tempPath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(testPath);

        ignoreFilePath = Path.Combine(testPath, ".ignore");
    }

    public void Dispose()
    {
        Directory.Delete(this.testPath, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Constructor_ShouldCreateIgnoreFileWithDefaultContent()
    {
        // Act
        var manager = new TrackingIgnoreManager(ignoreFilePath);
        manager.IncludeTextFormat();

        // Assert
        Assert.True(File.Exists(ignoreFilePath));
        var content = File.ReadAllText(ignoreFilePath);
        Assert.Contains("*", content);
        Assert.Contains("!*.txt", content);
        Assert.Contains("!*.md", content);
        Assert.Contains("!*.docx", content);
        Assert.Contains("!*.xlsx", content);
        Assert.Contains("!*.pptx", content);
        Assert.Contains("!*.pdf", content);
    }

    [Fact]
    public void IsIgnored_ShouldIgnoreAllFilesExceptSpecified()
    {
        // Arrange
        var manager = new TrackingIgnoreManager(ignoreFilePath);
        manager.IncludeTextFormat();

        // Act & Assert
        Assert.False(manager.IsIgnored(Path.Combine(testPath, "file.txt")));
        Assert.False(manager.IsIgnored(Path.Combine(testPath, "document.md")));
        Assert.False(manager.IsIgnored(Path.Combine(testPath, "spreadsheet.xlsx")));
        Assert.False(manager.IsIgnored(Path.Combine(testPath, "presentation.pptx")));
        Assert.False(manager.IsIgnored(Path.Combine(testPath, "report.pdf")));
        Assert.True(manager.IsIgnored(Path.Combine(testPath, "script.js")));
        Assert.True(manager.IsIgnored(Path.Combine(testPath, "image.png")));
    }

    [Fact]
    public void AddRule_ShouldAddNewRuleToIgnoreFile()
    {
        // Arrange
        var manager = new TrackingIgnoreManager(ignoreFilePath);
        manager.IncludeTextFormat();

        var newRule = "!*.csv";

        // Act
        manager.AddRules(newRule);

        // Assert
        var rules = manager.GetRules().ToList();
        Assert.Contains(newRule, rules);

        var fileContent = File.ReadAllText(ignoreFilePath);
        Assert.Contains(newRule, fileContent);

        Assert.False(manager.IsIgnored(Path.Combine(testPath, "data.csv")));
    }

    [Fact]
    public void RemoveRule_ShouldRemoveExistingRuleFromIgnoreFile()
    {
        // Arrange
        var manager = new TrackingIgnoreManager(ignoreFilePath);
        manager.IncludeTextFormat();

        var ruleToRemove = "!*.txt";

        // Act
        manager.RemoveRules(ruleToRemove);

        // Assert
        var rules = manager.GetRules().ToList();
        Assert.DoesNotContain(ruleToRemove, rules);

        var fileContent = File.ReadAllText(ignoreFilePath);
        Assert.DoesNotContain(ruleToRemove, fileContent);

        Assert.True(manager.IsIgnored(Path.Combine(testPath, "file.txt")));
    }

    [Fact]
    public void GetRules_ShouldReturnAllNonCommentRules()
    {
        // Arrange
        var manager = new TrackingIgnoreManager(ignoreFilePath);
        manager.IncludeTextFormat();

        // Act
        var rules = manager.GetRules().ToList();

        // Assert
        Assert.Contains("*", rules);
        Assert.Contains("!*.txt", rules);
        Assert.Contains("!*.md", rules);
        Assert.Contains("!*.docx", rules);
        Assert.Contains("!*.xlsx", rules);
        Assert.Contains("!*.pptx", rules);
        Assert.Contains("!*.pdf", rules);
        Assert.DoesNotContain(rules, r => r.StartsWith('#') && r != "* # All Ignore");
    }

    [Fact]
    public async Task IsIgnored_ShouldRespectNestedIgnoreFiles()
    {
        // Arrange
        var manager = new TrackingIgnoreManager(ignoreFilePath);
        manager.IncludeTextFormat();

        var subDir = Path.Combine(testPath, "subdir");
        Directory.CreateDirectory(subDir);
        var subIgnoreFilePath = Path.Combine(subDir, ".ignore");
        File.WriteAllText(subIgnoreFilePath, "*.txt");

        await Task.Delay(TimeSpan.FromSeconds(2));

        // Act & Assert
        Assert.False(manager.IsIgnored(Path.Combine(testPath, "file.txt")));
        Assert.True(manager.IsIgnored(Path.Combine(subDir, "file.txt")));
        Assert.False(manager.IsIgnored(Path.Combine(subDir, "file.md")));
    }
}
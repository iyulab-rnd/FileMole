using FileMoles.Internal;

namespace FileMoles.Tests;

public class IgnoreManagerTests : IDisposable
{
    private readonly string testPath;
    private readonly string ignoreFilePath;

    public IgnoreManagerTests()
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
    public void IgnoreManager_ShouldIgnoreAndIncludeFilesBasedOnPatterns()
    {
        // 테스트 파일 및 디렉토리 생성
        var file1 = Path.Combine(testPath, "file1.txt");
        var file2 = Path.Combine(testPath, "file2.log");
        var subDir = Path.Combine(testPath, "subdir");
        var subFile = Path.Combine(subDir, "file3.txt");

        File.WriteAllText(file1, "Test file 1");
        File.WriteAllText(file2, "Test file 2");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(subFile, "Test file 3");

        // .ignore 파일 생성
        File.WriteAllText(ignoreFilePath, @"
# 모든 파일을 무시
*

# .txt 파일 포함
!*.txt

# subdir 디렉토리 내 모든 파일 포함
!subdir/
");

        // IgnoreManager 인스턴스 생성
        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // 파일이 올바르게 무시되거나 포함되는지 테스트
        Assert.False(ignoreManager.IsIgnored(file1), "file1.txt는 포함되어야 합니다.");
        Assert.True(ignoreManager.IsIgnored(file2), "file2.log는 무시되어야 합니다.");
        Assert.False(ignoreManager.IsIgnored(subFile), "subdir/file3.txt는 포함되어야 합니다.");
    }

    [Fact]
    public void IgnoreManager_ShouldOverridePatternsInSubdirectory()
    {
        // 상위 디렉토리의 .ignore 파일 생성
        File.WriteAllText(ignoreFilePath, @"
# 모든 파일 무시
*
");

        // 하위 디렉토리 및 파일 생성
        var subDir = Path.Combine(testPath, "subdir");
        Directory.CreateDirectory(subDir);
        var subFile1 = Path.Combine(subDir, "file1.txt");
        var subFile2 = Path.Combine(subDir, "file2.log");
        File.WriteAllText(subFile1, "Subdir file 1");
        File.WriteAllText(subFile2, "Subdir file 2");

        // 하위 디렉토리의 .ignore 파일 생성
        var subIgnoreFilePath = Path.Combine(subDir, ".ignore");
        File.WriteAllText(subIgnoreFilePath, @"
# 모든 파일 포함
!*

# .log 파일 무시
*.log
");

        // IgnoreManager 인스턴스 생성
        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // 파일이 올바르게 무시되거나 포함되는지 테스트
        Assert.True(ignoreManager.IsIgnored(Path.Combine(testPath, "somefile.txt")), "상위 디렉토리의 파일은 무시되어야 합니다.");
        Assert.False(ignoreManager.IsIgnored(subFile1), "subdir/file1.txt는 포함되어야 합니다.");
        Assert.True(ignoreManager.IsIgnored(subFile2), "subdir/file2.log는 무시되어야 합니다.");
    }

    [Fact]
    public void IgnoreManager_ShouldCreateIgnoreFileIfNotExists()
    {
        // .ignore 파일 삭제
        File.Delete(ignoreFilePath);

        // IgnoreManager 인스턴스 생성 (파일이 없으므로 생성되어야 함)
        _ = IgnoreManager.CreateNew(ignoreFilePath);

        // .ignore 파일이 생성되었는지 확인
        Assert.True(File.Exists(ignoreFilePath), ".ignore 파일이 생성되어야 합니다.");
    }

    [Fact]
    public void IgnoreManager_ShouldHandleEmptyIgnoreFile()
    {
        // Create an empty .ignore file
        File.WriteAllText(ignoreFilePath, string.Empty);

        // Create a test file
        var file = Path.Combine(testPath, "file.txt");
        File.WriteAllText(file, "Test file");

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // The file should not be ignored
        Assert.False(ignoreManager.IsIgnored(file), "file.txt should not be ignored when .ignore file is empty.");
    }

    [Fact]
    public void IgnoreManager_ShouldHandleOnlyCommentsInIgnoreFile()
    {
        // Create a .ignore file with only comments
        File.WriteAllText(ignoreFilePath, @"
# This is a comment
# Another comment line
");

        // Create a test file
        var file = Path.Combine(testPath, "file.txt");
        File.WriteAllText(file, "Test file");

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // The file should not be ignored
        Assert.False(ignoreManager.IsIgnored(file), "file.txt should not be ignored when .ignore file contains only comments.");
    }

    [Fact]
    public void IgnoreManager_ShouldIgnoreFilesWithComplexPatterns()
    {
        // Create a .ignore file with complex patterns
        File.WriteAllText(ignoreFilePath, @"
# Ignore all .log files
*.log

# Ignore files starting with 'temp' and ending with '.txt'
temp*.txt

# Ignore files in 'build' directory
build/**

# Include 'build/keep.txt' even though 'build/' is ignored
!build/keep.txt
");

        // Create test files and directories
        var logFile = Path.Combine(testPath, "app.log");
        var tempFile = Path.Combine(testPath, "temp123.txt");
        var buildDir = Path.Combine(testPath, "build");
        Directory.CreateDirectory(buildDir);
        var buildFile1 = Path.Combine(buildDir, "file.o");
        var buildFile2 = Path.Combine(buildDir, "keep.txt");

        File.WriteAllText(logFile, "Log file");
        File.WriteAllText(tempFile, "Temporary file");
        File.WriteAllText(buildFile1, "Build file");
        File.WriteAllText(buildFile2, "File to keep");

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // Test files are correctly ignored or included
        Assert.True(ignoreManager.IsIgnored(logFile), "app.log should be ignored.");
        Assert.True(ignoreManager.IsIgnored(tempFile), "temp123.txt should be ignored.");
        Assert.True(ignoreManager.IsIgnored(buildFile1), "build/file.o should be ignored.");
        Assert.False(ignoreManager.IsIgnored(buildFile2), "build/keep.txt should be included.");
    }

    [Fact]
    public void IgnoreManager_ShouldHandleSpecialCharactersInFileNames()
    {
        // Create a .ignore file
        File.WriteAllText(ignoreFilePath, @"
# Ignore files with spaces
special file.txt

# Ignore files with unicode characters
*.日本
");

        // Create test files
        var fileWithSpace = Path.Combine(testPath, "special file.txt");
        var unicodeFile = Path.Combine(testPath, "file.日本");
        var normalFile = Path.Combine(testPath, "normal.txt");

        File.WriteAllText(fileWithSpace, "File with space");
        File.WriteAllText(unicodeFile, "Unicode file");
        File.WriteAllText(normalFile, "Normal file");

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // Test files are correctly ignored or included
        Assert.True(ignoreManager.IsIgnored(fileWithSpace), "File with spaces should be ignored.");
        Assert.True(ignoreManager.IsIgnored(unicodeFile), "Unicode file should be ignored.");
        Assert.False(ignoreManager.IsIgnored(normalFile), "Normal file should not be ignored.");
    }

    [Fact]
    public async Task IgnoreManager_ShouldHandleNegationPatternsCorrectly()
    {
        // Create a .ignore file
        File.WriteAllText(ignoreFilePath, @"
# Ignore all .txt files
*.txt

# But include important.txt
!important.txt
");

        // Create test files
        var file1 = Path.Combine(testPath, "note.txt");
        var file2 = Path.Combine(testPath, "important.txt");
        var file3 = Path.Combine(testPath, "data.log");

        File.WriteAllText(file1, "Note file");
        File.WriteAllText(file2, "Important file");
        File.WriteAllText(file3, "Log file");

        var ignoreManager = await IgnoreManager.CreateAsync(ignoreFilePath);

        // Test files are correctly ignored or included
        Assert.True(ignoreManager.IsIgnored(file1), "note.txt should be ignored.");
        Assert.False(ignoreManager.IsIgnored(file2), "important.txt should be included.");
        Assert.False(ignoreManager.IsIgnored(file3), "data.log should not be ignored.");
    }

    [Fact]
    public void IgnoreManager_ShouldHandleDirectoryIgnorePatterns()
    {
        // Create a .ignore file
        File.WriteAllText(ignoreFilePath, @"
# Ignore the 'logs' directory
logs/**

# Include 'logs/keep.log'
!logs/keep.log
");

        // Create test files and directories
        var logsDir = Path.Combine(testPath, "logs");
        Directory.CreateDirectory(logsDir);
        var logFile1 = Path.Combine(logsDir, "error.log");
        var logFile2 = Path.Combine(logsDir, "keep.log");

        File.WriteAllText(logFile1, "Error log");
        File.WriteAllText(logFile2, "Important log");

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // Test files are correctly ignored or included
        Assert.True(ignoreManager.IsIgnored(logFile1), "logs/error.log should be ignored.");
        Assert.False(ignoreManager.IsIgnored(logFile2), "logs/keep.log should be included.");
    }


    [Fact]
    public void IgnoreManager_ShouldHandleNestedDirectories()
    {
        // Create a .ignore file
        File.WriteAllText(ignoreFilePath, @"
# Ignore all files in 'src' directory
src/**

# Include 'src/main/**'
!src/main/**

# Ignore 'src/main/test/**'
src/main/test/**
");

        // Create directories and files
        var srcDir = Path.Combine(testPath, "src");
        var mainDir = Path.Combine(srcDir, "main");
        var testDir = Path.Combine(mainDir, "test");
        Directory.CreateDirectory(testDir);

        var file1 = Path.Combine(srcDir, "util.cs");
        var file2 = Path.Combine(mainDir, "app.cs");
        var file3 = Path.Combine(testDir, "app_test.cs");

        File.WriteAllText(file1, "Utility code");
        File.WriteAllText(file2, "Application code");
        File.WriteAllText(file3, "Test code");

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // Test files are correctly ignored or included
        Assert.True(ignoreManager.IsIgnored(file1), "src/util.cs should be ignored.");
        Assert.False(ignoreManager.IsIgnored(file2), "src/main/app.cs should be included.");
        Assert.True(ignoreManager.IsIgnored(file3), "src/main/test/app_test.cs should be ignored.");
    }

    [Fact]
    public void IgnoreManager_ShouldHandleInvalidPatternsGracefully()
    {
        // Create a .ignore file with an invalid pattern
        File.WriteAllText(ignoreFilePath, @"
[invalid pattern
");

        // Create a test file
        var file = Path.Combine(testPath, "file.txt");
        File.WriteAllText(file, "Test file");

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // The file should not be ignored despite the invalid pattern
        Assert.False(ignoreManager.IsIgnored(file), "file.txt should not be ignored even if .ignore contains invalid patterns.");
    }

    [Fact]
    public void IgnoreManager_ShouldHandleFilesWithoutExtension()
    {
        // Create a .ignore file
        File.WriteAllText(ignoreFilePath, @"
# Ignore all files
*

# Include files with extensions
!*.*

# Include 'README'
!README
");

        // Create test files
        var file1 = Path.Combine(testPath, "script");
        var file2 = Path.Combine(testPath, "README");
        var file3 = Path.Combine(testPath, "notes.txt");

        File.WriteAllText(file1, "Executable script");
        File.WriteAllText(file2, "Readme file");
        File.WriteAllText(file3, "Notes");

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // Test files are correctly ignored or included
        Assert.True(ignoreManager.IsIgnored(file1), "script should be ignored.");
        Assert.False(ignoreManager.IsIgnored(file2), "README should be included.");
        Assert.False(ignoreManager.IsIgnored(file3), "notes.txt should not be ignored.");
    }

    [Fact]
    public void IgnoreManager_ShouldHandleMultipleNegationPatterns()
    {
        // Create a .ignore file
        File.WriteAllText(ignoreFilePath, @"
# Ignore all files
*

# Include all .txt files
!*.txt

# But ignore secret.txt
secret.txt
");

        // Create test files
        var file1 = Path.Combine(testPath, "document.txt");
        var file2 = Path.Combine(testPath, "secret.txt");
        var file3 = Path.Combine(testPath, "image.png");

        File.WriteAllText(file1, "Document");
        File.WriteAllText(file2, "Secret");
        File.WriteAllText(file3, "Image");

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // Test files are correctly ignored or included
        Assert.False(ignoreManager.IsIgnored(file1), "document.txt should be included.");
        Assert.True(ignoreManager.IsIgnored(file2), "secret.txt should be ignored.");
        Assert.True(ignoreManager.IsIgnored(file3), "image.png should be ignored.");
    }

    [Fact]
    public void IgnoreManager_ShouldNotIgnoreDirectoriesWhenPatternIsForFiles()
    {
        // Create a .ignore file
        File.WriteAllText(ignoreFilePath, @"
# Ignore all .log files
*.log
");

        // Create directories and files
        var logDir = Path.Combine(testPath, "logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(testPath, "error.log");
        var logDirFile = Path.Combine(logDir, "latest.log");

        File.WriteAllText(logFile, "Error log");
        File.WriteAllText(logDirFile, "Latest log");

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // Test files are correctly ignored or included
        Assert.True(ignoreManager.IsIgnored(logFile), "error.log should be ignored.");
        Assert.False(ignoreManager.IsIgnored(logDir), "logs directory should not be ignored.");
        Assert.True(ignoreManager.IsIgnored(logDirFile), "logs/latest.log should be ignored.");
    }

    [Fact]
    public void IgnoreManager_ShouldIgnoreFilesInIgnoredDirectories()
    {
        // Create a .ignore file
        File.WriteAllText(ignoreFilePath, @"
# Ignore the 'bin' directory
bin/
");

        // Create directories and files
        var binDir = Path.Combine(testPath, "bin");
        Directory.CreateDirectory(binDir);
        var binFile = Path.Combine(binDir, "app.exe");

        File.WriteAllText(binFile, "Executable");

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // Test files are correctly ignored or included
        Assert.True(ignoreManager.IsIgnored(binDir), "bin directory should be ignored.");
        Assert.True(ignoreManager.IsIgnored(binFile), "bin/app.exe should be ignored.");
    }

    [Fact]
    public void IgnoreManager_ShouldIncludeIgnoredDirectory()
    {
        // Create a .ignore file
        File.WriteAllText(ignoreFilePath, @"
# Ignore the 'bin' directory
bin/

# But include 'bin/include_me.txt'
!bin/include_me.txt
");

        // Create directories and files
        var binDir = Path.Combine(testPath, "bin");
        Directory.CreateDirectory(binDir);
        var binFile1 = Path.Combine(binDir, "app.exe");
        var binFile2 = Path.Combine(binDir, "include_me.txt");

        File.WriteAllText(binFile1, "Executable");
        File.WriteAllText(binFile2, "Include me");

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // Test files are correctly ignored or included
        Assert.True(ignoreManager.IsIgnored(binDir), "bin directory should be ignored.");
        Assert.True(ignoreManager.IsIgnored(binFile1), "bin/app.exe should be ignored.");
        Assert.False(ignoreManager.IsIgnored(binFile2), "bin/include_me.txt should be included.");
    }

    [Fact]
    public void IgnoreManager_ShouldIncludeFilesInIncludedDirectories()
    {
        // Create a .ignore file
        File.WriteAllText(ignoreFilePath, @"
# Ignore all files
*

# Include the 'src' directory
!src/
");

        // Create directories and files
        var srcDir = Path.Combine(testPath, "src");
        Directory.CreateDirectory(srcDir);
        var srcFile = Path.Combine(srcDir, "main.cs");
        var rootFile = Path.Combine(testPath, "readme.md");

        File.WriteAllText(srcFile, "Source code");
        File.WriteAllText(rootFile, "Root readme");

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);

        // Test files are correctly ignored or included
        Assert.False(ignoreManager.IsIgnored(srcDir), "src directory should be included.");
        Assert.False(ignoreManager.IsIgnored(srcFile), "src/main.cs should be included.");
        Assert.True(ignoreManager.IsIgnored(rootFile), "readme.md should be ignored.");
    }

    [Fact]
    public async Task AddRule_ShouldAddNewRuleToIgnoreFile()
    {
        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);
        var newRule = "*.log";

        await ignoreManager.AddRulesAsync(newRule);

        var rules = ignoreManager.GetRules().ToList();
        Assert.Contains(newRule, rules);

        // Verify that the rule was actually added to the file
        var fileContent = File.ReadAllText(ignoreFilePath);
        Assert.Contains(newRule, fileContent);
    }

    [Fact]
    public async Task AddRule_ShouldImmediatelyApplyNewRule()
    {
        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);
        var newRule = "*.log";
        var testFile = Path.Combine(testPath, "test.log");
        File.WriteAllText(testFile, "Test log file");

        Assert.False(ignoreManager.IsIgnored(testFile));

        await ignoreManager.AddRulesAsync(newRule);

        Assert.True(ignoreManager.IsIgnored(testFile));
    }

    [Fact]
    public async Task RemoveRule_ShouldRemoveExistingRuleFromIgnoreFile()
    {
        var initialRules = new[] { "*.txt", "*.log" };
        File.WriteAllLines(ignoreFilePath, initialRules);

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);
        await ignoreManager.RemoveRulesAsync("*.log");

        var rules = ignoreManager.GetRules().ToList();
        Assert.DoesNotContain("*.log", rules);
        Assert.Contains("*.txt", rules);

        // Verify that the rule was actually removed from the file
        var fileContent = File.ReadAllText(ignoreFilePath);
        Assert.DoesNotContain("*.log", fileContent);
        Assert.Contains("*.txt", fileContent);
    }

    [Fact]
    public async Task RemoveRule_ShouldImmediatelyApplyRuleRemoval()
    {
        var initialRules = new[] { "*.txt", "*.log" };
        File.WriteAllLines(ignoreFilePath, initialRules);

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);
        var testFile = Path.Combine(testPath, "test.log");
        File.WriteAllText(testFile, "Test log file");

        Assert.True(ignoreManager.IsIgnored(testFile));

        await ignoreManager.RemoveRulesAsync("*.log");

        Assert.False(ignoreManager.IsIgnored(testFile));
    }

    [Fact]
    public void GetRules_ShouldReturnAllNonCommentRules()
    {
        var rules = new[]
        {
            "# This is a comment",
            "*.txt",
            "",
            "# Another comment",
            "!important.doc",
            "temp/"
        };
        File.WriteAllLines(ignoreFilePath, rules);

        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);
        var retrievedRules = ignoreManager.GetRules().ToList();

        Assert.Equal(3, retrievedRules.Count);
        Assert.Contains("*.txt", retrievedRules);
        Assert.Contains("!important.doc", retrievedRules);
        Assert.Contains("temp/", retrievedRules);
        Assert.DoesNotContain("# This is a comment", retrievedRules);
        Assert.DoesNotContain("# Another comment", retrievedRules);
        Assert.DoesNotContain("", retrievedRules);
    }

    [Fact]
    public async Task AddRule_ShouldThrowExceptionForEmptyRule()
    {
        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);
        await Assert.ThrowsAsync<ArgumentException>(() => ignoreManager.AddRulesAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => ignoreManager.AddRulesAsync("  "));
    }

    [Fact]
    public async Task RemoveRule_ShouldThrowExceptionForEmptyRule()
    {
        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);
        await Assert.ThrowsAsync<ArgumentException>(() => ignoreManager.RemoveRulesAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => ignoreManager.RemoveRulesAsync("  "));
    }

    [Fact]
    public async Task RemoveRule_ShouldNotThrowExceptionForNonExistentRule()
    {
        var ignoreManager = IgnoreManager.CreateNew(ignoreFilePath);
        var initialRules = ignoreManager.GetRules().ToList();

        await ignoreManager.RemoveRulesAsync("non_existent_rule");

        var finalRules = ignoreManager.GetRules().ToList();
        Assert.Equal(initialRules, finalRules);
    }
}

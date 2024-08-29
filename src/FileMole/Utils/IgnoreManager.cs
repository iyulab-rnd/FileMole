using System.Text.RegularExpressions;

namespace FileMole.Utils;

internal class IgnoreManager
{
    private readonly HashSet<string> _ignoredPaths = [];
    private readonly List<Regex> _ignoredPatterns = [];

    public IgnoreManager()
    {
        var basePath = Functions.GetFileMoleDataPath();
        LoadIgnoreFile(Path.Combine(basePath, "filemole.ignore"));
        AddDefaultIgnores();
    }

    private void LoadIgnoreFile(string path)
    {
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                {
                    AddIgnorePattern(line.Trim());
                }
            }
        }
    }

    private void AddDefaultIgnores()
    {
        // FileMole specific files
        AddIgnorePattern(Functions.GetDatabasePath());
        AddIgnorePattern("*.db-journal");

        // Version control
        AddIgnorePattern(".git");
        AddIgnorePattern(".svn");
        AddIgnorePattern(".hg");

        // IDE and editor files
        AddIgnorePattern(".vs");
        AddIgnorePattern(".vscode");
        AddIgnorePattern("*.suo");
        AddIgnorePattern("*.user");
        AddIgnorePattern("*.userosscache");
        AddIgnorePattern("*.sln.docstates");

        // Build results
        AddIgnorePattern("bin");
        AddIgnorePattern("obj");

        // Package management
        AddIgnorePattern("packages");
        AddIgnorePattern("node_modules");

        // Logs and databases
        AddIgnorePattern("*.log");
        AddIgnorePattern("*.sqlite");

        // OS generated files
        AddIgnorePattern(".DS_Store");
        AddIgnorePattern("Thumbs.db");

        // Temporary files
        AddIgnorePattern("*.tmp");
        AddIgnorePattern("*.temp");
        AddIgnorePattern("~$*");

        // Backup files
        AddIgnorePattern("*.bak");
        AddIgnorePattern("*.swp");

        // .NET specific
        AddIgnorePattern("project.lock.json");
        AddIgnorePattern("project.fragment.lock.json");
        AddIgnorePattern("artifacts");

        // Others
        AddIgnorePattern("*.cache");
        AddIgnorePattern("*.orig");
    }

    public void AddIgnorePattern(string pattern)
    {
        if (Path.IsPathRooted(pattern))
        {
            _ignoredPaths.Add(Path.GetFullPath(pattern));
        }
        else
        {
            _ignoredPatterns.Add(new Regex(WildcardToRegex(pattern), RegexOptions.IgnoreCase));
        }
    }

    public bool ShouldIgnore(string path)
    {
        if (_ignoredPaths.Contains(Path.GetFullPath(path)))
            return true;

        var fileName = Path.GetFileName(path);
        return _ignoredPatterns.Any(regex => regex.IsMatch(fileName) || regex.IsMatch(path));
    }

    private static string WildcardToRegex(string pattern)
    {
        return "^" + Regex.Escape(pattern)
                          .Replace("\\*", ".*")
                          .Replace("\\?", ".") + "$";
    }
}
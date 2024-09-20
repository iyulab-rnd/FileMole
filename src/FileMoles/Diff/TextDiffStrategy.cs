using DiffPlex.DiffBuilder.Model;
using DiffPlex.DiffBuilder;
using DiffPlex;
using System.Text;

namespace FileMoles.Diff;

public class TextDiffResult : DiffResult
{
    public List<TextDiffEntry> Entries { get; set; } = [];

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var entry in Entries)
        {
            sb.AppendLine($"[{entry.Type}] {entry.StartPosition}-{entry.EndPosition}: {entry.OriginalText}");
        }

        return sb.ToString();
    }
}

public class TextDiffEntry
{
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
    public string? OriginalText { get; set; }
    public string? ModifiedText { get; set; }
    public DiffType Type { get; set; }
}

public enum DiffType
{
    Inserted,
    Deleted,
    Modified,
    Unchanged
}

public class TextDiffStrategy : IDiffStrategy
{
    public async Task<DiffResult> GenerateDiffAsync(string oldFilePath, string newFilePath, CancellationToken cancellationToken = default)
    {
        var oldText = await File.ReadAllTextAsync(oldFilePath, cancellationToken);
        var newText = await File.ReadAllTextAsync(newFilePath, cancellationToken);

        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(oldText, newText);

        var result = new TextDiffResult { FileType = "Text" };

        int position = 0;
        foreach (var line in diff.Lines)
        {
            var entry = new TextDiffEntry
            {
                StartPosition = position,
                EndPosition = position + line.Text.Length,
                OriginalText = line.Text,
                ModifiedText = line.Text,
                Type = ConvertDiffType(line.Type)
            };

            result.Entries.Add(entry);
            position += line.Text.Length + 1; // +1 for newline
        }

        result.IsChanged = result.Entries.Any(e => e.Type != DiffType.Unchanged);

        return result;
    }

    private static DiffType ConvertDiffType(ChangeType changeType)
    {
        return changeType switch
        {
            ChangeType.Inserted => DiffType.Inserted,
            ChangeType.Deleted => DiffType.Deleted,
            ChangeType.Modified => DiffType.Modified,
            _ => DiffType.Unchanged,
        };
    }
}

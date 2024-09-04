﻿using DiffPlex.DiffBuilder.Model;
using DiffPlex.DiffBuilder;
using DiffPlex;

namespace FileMoles.Diff;

public class TextDiffResult : DiffResult
{
    public List<TextDiffEntry> Entries { get; set; } = [];
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
    public async Task<DiffResult> GenerateDiffAsync(string oldFilePath, string newFilePath)
    {
        var oldText = await File.ReadAllTextAsync(oldFilePath);
        var newText = await File.ReadAllTextAsync(newFilePath);

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

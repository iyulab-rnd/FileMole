﻿using DiffPlex.DiffBuilder.Model;
using DiffPlex.DiffBuilder;
using DiffPlex;

namespace FileMoles.Diff;

public class OdfDiffResult : DiffResult
{
    public List<OdfDiffEntry> Entries { get; set; } = new List<OdfDiffEntry>();
}

public class OdfDiffEntry
{
    public string ElementType { get; set; } // e.g., "Paragraph", "Cell", etc.
    public string ElementIdentifier { get; set; } // e.g., paragraph number, cell reference
    public List<TextDiffEntry> TextDiffs { get; set; } = new List<TextDiffEntry>();
}

public class OdfDiffStrategy : IDiffStrategy
{
    public async Task<DiffResult> GenerateDiffAsync(string oldFilePath, string newFilePath)
    {
        var oldContent = await ExtractContentFromOdfAsync(oldFilePath);
        var newContent = await ExtractContentFromOdfAsync(newFilePath);

        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(oldContent, newContent);

        var result = new OdfDiffResult { FileType = "ODF" };

        string currentElement = "";
        int elementCount = 0;
        var currentElementDiffs = new List<TextDiffEntry>();

        foreach (var line in diff.Lines)
        {
            if (line.Text.StartsWith("%%% Element: "))
            {
                if (currentElementDiffs.Count > 0)
                {
                    result.Entries.Add(new OdfDiffEntry
                    {
                        ElementType = currentElement,
                        ElementIdentifier = elementCount.ToString(),
                        TextDiffs = currentElementDiffs
                    });
                }
                currentElement = line.Text.Substring(13);
                elementCount++;
                currentElementDiffs = new List<TextDiffEntry>();
            }
            else
            {
                var entry = new TextDiffEntry
                {
                    StartPosition = 0, // We don't have precise positions in ODF
                    EndPosition = line.Text.Length,
                    OriginalText = line.Text,
                    ModifiedText = line.Text,
                    Type = ConvertDiffType(line.Type)
                };

                currentElementDiffs.Add(entry);
            }
        }

        // Add the last element
        if (currentElementDiffs.Count > 0)
        {
            result.Entries.Add(new OdfDiffEntry
            {
                ElementType = currentElement,
                ElementIdentifier = elementCount.ToString(),
                TextDiffs = currentElementDiffs
            });
        }

        return result;
    }

    private async Task<string> ExtractContentFromOdfAsync(string filePath)
    {
        // Implementation depends on the ODF library you're using
        // This is a placeholder
        return await Task.FromResult("Extracted ODF content");
    }

    private DiffType ConvertDiffType(ChangeType changeType)
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
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FileMoles.Diff;

public interface IDiffStrategy
{
    Task<DiffResult> GenerateDiffAsync(string oldFilePath, string newFilePath);
}

public abstract class DiffResult
{
    public string FileType { get; set; }
}
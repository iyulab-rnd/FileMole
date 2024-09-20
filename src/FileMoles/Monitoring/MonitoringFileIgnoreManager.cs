//using FileMoles.Internal;
//using GlobExpressions;
//using Microsoft.Extensions.Logging;

//namespace FileMoles.Monitoring;

//public class MonitoringFileIgnoreManager
//{
//    private readonly List<(Glob Pattern, bool IsInclude)> _patterns = [];
//    private readonly string _configPath;
//    private readonly string _dataPath;

//    public MonitoringFileIgnoreManager(string dataPath)
//    {
//        _dataPath = dataPath;
//        _configPath = Path.Combine(dataPath, Constants.IgnoreFileName);

//        if (!File.Exists(_configPath))
//        {
//            File.WriteAllText(_configPath, GetDefaultPatternsText());
//        }
//        LoadPatterns(_configPath);
//    }

//    private void LoadPatterns(string path)
//    {
//        foreach (var line in File.ReadLines(path))
//        {
//            var trimmedLine = line.Trim();
//            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith('#'))
//                continue;

//            bool isInclude = trimmedLine.StartsWith('!');
//            string pattern = isInclude ? trimmedLine[1..] : trimmedLine;
//            string adjustedPattern = AdjustPattern(pattern);

//            try
//            {
//                var glob = new Glob(adjustedPattern, GlobOptions.CaseInsensitive);
//                _patterns.Add((glob, isInclude));
//            }
//            catch (GlobPatternException ex)
//            {
//                Logger.Error($"Invalid glob pattern '{pattern}' in config: {ex.Message}");
//            }
//        }
//    }


//    /// <summary>
//    /// 패턴을 추가합니다. 패턴의 첫 문자가 '!'이면 포함 패턴으로, 그렇지 않으면 무시 패턴으로 처리됩니다.
//    /// </summary>
//    /// <param name="pattern">추가할 패턴</param>
//    public void AddPattern(string pattern)
//    {
//        File.AppendAllText(_configPath, Environment.NewLine + pattern);
//        bool isInclude = pattern.StartsWith('!');
//        string p = isInclude ? pattern[1..] : pattern;
//        string adjustedPattern = AdjustPattern(p);

//        try
//        {
//            var glob = new Glob(adjustedPattern, GlobOptions.CaseInsensitive);
//            _patterns.Add((glob, isInclude));
//        }
//        catch (GlobPatternException ex)
//        {
//            Logger.Error($"Invalid glob pattern '{pattern}': {ex.Message}");
//        }
//    }

//    /// <summary>
//    /// 주어진 경로가 무시 대상인지 여부를 반환합니다.
//    /// </summary>
//    /// <param name="path">확인할 파일 경로</param>
//    /// <returns>무시해야 하면 true, 아니면 false</returns>
//    public bool ShouldIgnore(string path)
//    {
//        string fullPath = IOHelper.NormalizePath(Path.Combine(_dataPath, path));
//        if (IsHiddenFileOrDirectory(fullPath))
//            return true;

//        string relativePath = Path.GetRelativePath(_dataPath, fullPath).Replace('\\', '/');

//        bool ignore = false;
//        foreach (var (pattern, isInclude) in _patterns)
//        {
//            try
//            {
//                if (pattern.IsMatch(relativePath))
//                {
//                    ignore = !isInclude;
//                }
//            }
//            catch (Exception)
//            {
//            }
//        }

//        return ignore;
//    }

//    /// <summary>
//    /// 현재 패턴들의 디버그 정보를 반환합니다.
//    /// </summary>
//    /// <returns>패턴 목록 문자열</returns>
//    public string GetPatternsDebugInfo() =>
//        $"Current patterns:{Environment.NewLine}{string.Join(Environment.NewLine, _patterns.Select(p => $"{(p.IsInclude ? "Include" : "Ignore")}: {p.Pattern.Pattern}"))}";

//    /// <summary>
//    /// 파일 또는 디렉터리가 숨김 속성인지 확인합니다.
//    /// </summary>
//    /// <param name="path">확인할 경로</param>
//    /// <returns>숨김이면 true, 아니면 false</returns>
//    private static bool IsHiddenFileOrDirectory(string path)
//    {
//        foreach (var part in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Skip(1))
//        {
//            path = Path.Combine(path, part);
//            if (IsHiddenEntry(path))
//                return true;
//        }
//        return false;
//    }

//    /// <summary>
//    /// 단일 파일 또는 디렉터리가 숨김 속성인지 확인합니다.
//    /// </summary>
//    /// <param name="path">확인할 경로</param>
//    /// <returns>숨김이면 true, 아니면 false</returns>
//    private static bool IsHiddenEntry(string path)
//    {
//        if (Path.GetFileName(path).StartsWith(".", StringComparison.OrdinalIgnoreCase))
//            return true;

//        try
//        {
//            if (File.Exists(path))
//            {
//                return new FileInfo(path).Attributes.HasFlag(FileAttributes.Hidden);
//            }
//            if (Directory.Exists(path))
//            {
//                var dirInfo = new DirectoryInfo(path);
//                return dirInfo.Parent != null && dirInfo.Attributes.HasFlag(FileAttributes.Hidden);
//            }
//        }
//        catch
//        {
//            // 접근 권한 문제 등으로 인한 예외 발생 시 무시
//        }

//        return false;
//    }

//    /// <summary>
//    /// 패턴을 조정하여 Glob 패턴 형식으로 변환합니다.
//    /// </summary>
//    /// <param name="pattern">원본 패턴</param>
//    /// <returns>조정된 패턴</returns>
//    private static string AdjustPattern(string pattern) =>
//        pattern.EndsWith('/')
//            ? $"**/{pattern}**"
//            : $"**/{pattern}";

//    /// <summary>
//    /// 기본 패턴 텍스트를 반환합니다.
//    /// </summary>
//    /// <returns>기본 패턴 문자열</returns>
//    /// <summary>
//    /// 기본 패턴 텍스트를 반환합니다.
//    /// </summary>
//    /// <returns>기본 패턴 문자열</returns>
//    private static string GetDefaultPatternsText() => @"
//# All Ignore 
//*

//# Include Document Formats
//!*.doc
//!*.docx
//!*.xls
//!*.xlsx
//!*.ppt
//!*.pptx
//!*.pdf
//!*.odt
//!*.ods
//!*.odp
//!*.rtf
//!*.tex
//!*.hwp
//!*.hwpx

//# Include Text Formats
//!*.txt
//!*.md
//!*.csv
//!*.json
//!*.xml
//!*.log
//!*.ini
//!*.cfg
//!*.conf

//# Include E-Book Formats
//!*.epub
//!*.mobi
//!*.azw3

//# Include Programming and Markup Languages
//!*.cs
//!*.java
//!*.py
//!*.js
//!*.html
//!*.css
//!*.php
//!*.rb
//!*.go
//!*.swift
//!*.kt
//!*.ts

//# Include Configuration and Script Files
//!*.sh
//!*.bat
//!*.ps1
//!*.yaml
//!*.yml
//!*.dockerfile
//!*.makefile

//# Include LaTeX Files
//!*.bib
//!*.cls
//!*.sty
//!*.dtx
//!*.ins

//# Include Other Text-Based Formats
//!*.properties
//!*.mdown
//!*.markdown
//!*.rst
//!*.adoc
//!*.asc

//# Include Image Formats
//!*.jpg
//!*.jpeg
//!*.png
//!*.gif
//!*.bmp
//!*.tiff
//!*.svg
//!*.webp
//!*.ico

//# Include Video Formats
//!*.mp4
//!*.mkv
//!*.avi
//!*.mov
//!*.wmv
//!*.flv
//!*.webm
//!*.mpeg
//!*.mpg
//!*.m4v

//# Include Audio Formats
//!*.mp3
//!*.wav
//!*.flac
//!*.aac
//!*.ogg
//!*.wma
//!*.m4a
//!*.aiff
//!*.alac
//!*.opus

//# Include Archive Formats
//!*.zip
//!*.rar
//!*.7z
//!*.tar
//!*.gz
//!*.bz2
//!*.xz
//!*.iso
//!*.cab
//!*.arj
//";
//}
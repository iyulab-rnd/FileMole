using FileMoles.Internal;

namespace FileMoles.Tracking;

internal class TrackingIgnoreManager(string ignoreFilePath) : IgnoreManager(ignoreFilePath)
{
    protected override string GetDefaultIgnoreContent()
    {
        return @"# 모든 파일을 무시
*

# 텍스트 기반 파일 추적 대상
!*.txt

# ODF
!*.docx
!*.xlsx
!*.pptx

# Print
!*.pdf
";
    }
}

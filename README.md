# FileMole

FileMole는 파일 시스템 변경을 감시하고 추적하는 강력한 .NET 라이브러리입니다. 두 가지 주요 기능을 제공합니다: 파일 시스템 감시와 파일 내용 추적.

## 주요 기능

### 1. 파일 시스템 감시 (File System Monitoring)

- **목적**: 지정된 디렉토리의 파일 및 폴더 변경사항을 감지하고 인덱싱하여 빠른 검색을 제공합니다.
- **주요 기능**:
  - 파일 및 폴더 생성, 수정, 삭제, 이름 변경 감지
  - 파일 메타데이터 인덱싱
  - 빠른 파일 검색

#### 사용 방법

```csharp
var builder = new FileMoleBuilder();
builder.AddMole("C:\\MyFolder", MoleType.Local);
var fileMole = builder.Build();

// 이벤트 구독
fileMole.FileCreated += (sender, e) => Console.WriteLine($"File created: {e.FullPath}");
fileMole.FileChanged += (sender, e) => Console.WriteLine($"File changed: {e.FullPath}");

// 파일 검색
var searchResults = await fileMole.SearchFilesAsync("example.txt");
```

### 2. 파일 내용 추적 (File Content Tracking)

- **목적**: 특정 파일의 내용 변경을 세밀하게 추적하고, 변경 내역을 제공합니다.
- **주요 기능**:
  - 파일 내용의 변경 사항 추적
  - 변경 내용에 대한 상세한 diff 정보 제공
  - 파일별 추적 설정 가능 (ignore 및 include 패턴 지원)

#### 사용 방법

```csharp
// 특정 파일이나 디렉토리에 대한 추적 활성화
await fileMole.EnableMoleTrackAsync("C:\\MyFolder\\important.txt");

// 추적 이벤트 구독
fileMole.FileContentChanged += (sender, e) => 
{
    Console.WriteLine($"Content changed in file: {e.FullPath}");
    Console.WriteLine($"Changes: {e.Diff}");
};

// 추적 설정
fileMole.AddIgnorePattern("C:\\MyFolder", "*.tmp");
fileMole.AddIncludePattern("C:\\MyFolder", "*.cs");
```

## 설치

NuGet 패키지 관리자를 통해 FileMole을 설치할 수 있습니다:

```
dotnet add package FileMole
```

## 주의사항

- 파일 시스템 감시는 대량의 파일을 다룰 때 시스템 리소스를 많이 사용할 수 있습니다. 필요한 디렉토리만 감시하도록 설정하세요.
- 파일 내용 추적은 추적 대상 파일의 백업 복사본을 생성합니다. 충분한 디스크 공간이 있는지 확인하세요.

## 라이선스

이 프로젝트는 MIT 라이선스 하에 배포됩니다. 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.
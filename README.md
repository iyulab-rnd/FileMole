네, 라이브러리의 업데이트된 내용을 반영하여 README 파일을 최신화하겠습니다. 다음은 업데이트된 README 내용입니다:

# FileMole

FileMole은 파일 시스템 변경을 감시하고 추적하는 강력한 .NET 라이브러리입니다. 파일 시스템 감시, 파일 내용 추적, 그리고 다양한 스토리지 유형 지원 등의 기능을 제공합니다.

## 주요 기능

### 1. 파일 시스템 감시 (File System Monitoring)

- 지정된 디렉토리의 파일 및 폴더 변경사항 감지 및 인덱싱
- 파일 및 폴더 생성, 수정, 삭제, 이름 변경 이벤트 제공
- 빠른 파일 검색 기능

```csharp
var builder = new FileMoleBuilder();
builder.AddMole("C:\\MyFolder", MoleType.Local);
var fileMole = builder.Build();

fileMole.FileCreated += (sender, e) => Console.WriteLine($"File created: {e.FullPath}");
fileMole.FileChanged += (sender, e) => Console.WriteLine($"File changed: {e.FullPath}");

var searchResults = await fileMole.SearchFilesAsync("example.txt");
```

### 2. 파일 내용 추적 (File Content Tracking)

- 특정 파일의 내용 변경 세밀 추적
- 변경 내용에 대한 상세한 diff 정보 제공
- 파일별 추적 설정 지원 (ignore 및 include 패턴)
- 디바운스 설정을 통한 이벤트 최적화

```csharp
// 추적 활성화
await fileMole.Tracking.EnableAsync("C:\\MyFolder\\important.txt");

// 디바운스 설정 (기본값: 1분)
fileMole.SetOptions(options => options.DebounceTime = 30000); // 30초로 설정

// 디바운스된 파일 내용 변경 이벤트 구독
fileMole.FileContentChanged += (sender, e) => 
{
    Console.WriteLine($"Content changed in file: {e.FullPath}");
    Console.WriteLine($"Changes: {e.Diff}");
};

// 추적 설정
fileMole.Config.AddIgnorePattern("*.tmp");
fileMole.Config.AddIncludePattern("*.cs");
```

파일 내용 추적 기능은 디바운스 메커니즘을 사용하여 성능을 최적화합니다. 디바운스는 짧은 시간 내에 발생하는 여러 변경 사항을 하나의 이벤트로 그룹화하여 처리합니다. 이는 다음과 같은 이점을 제공합니다:

1. **성능 향상**: 파일이 빠르게 여러 번 변경될 때 각 변경마다 이벤트를 발생시키는 대신, 마지막 변경 후 일정 시간이 지난 후에 하나의 이벤트만 발생시킵니다.

2. **리소스 사용 최적화**: 불필요한 처리와 I/O 작업을 줄여 시스템 리소스 사용을 최적화합니다.

3. **의미 있는 변경 감지**: 파일의 임시 저장이나 중간 상태가 아닌, 최종적인 변경 상태만을 감지하여 처리할 수 있습니다.

디바운스 시간은 `FileMoleOptions`의 `DebounceTime` 속성을 통해 설정할 수 있으며, 기본값은 1분(60000 밀리초)입니다. 이 값을 조정하여 애플리케이션의 요구사항에 맞게 최적화할 수 있습니다.

### 3. 다양한 스토리지 지원

- 로컬 스토리지, 원격 스토리지, 클라우드 스토리지(OneDrive, Google Drive) 지원
- 스토리지 유형에 따른 자동 프로바이더 선택

```csharp
builder.AddMole("C:\\LocalFolder", MoleType.Local);
builder.AddMole("https://remote-server.com/folder", MoleType.Remote);
builder.AddMole("OneDrive:/MyFolder", MoleType.Cloud, "OneDrive");
```

## 설치

NuGet 패키지 관리자를 통해 FileMole을 설치할 수 있습니다:

```
dotnet add package FileMole
```

## 주의사항

- 파일 시스템 감시는 대량의 파일을 다룰 때 시스템 리소스를 많이 사용할 수 있습니다. 필요한 디렉토리만 감시하도록 설정하세요.
- 파일 내용 추적은 추적 대상 파일의 백업 복사본을 생성합니다. 충분한 디스크 공간이 있는지 확인하세요.
- 원격 및 클라우드 스토리지 사용 시 네트워크 상태와 대역폭을 고려하세요.
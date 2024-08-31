# FileMole

FileMole은 파일 시스템을 손쉽게 모니터링하고 관리할 수 있는 .NET 라이브러리입니다. (.NET8)

## FileMole의 의미와 배경

FileMole이라는 이름은 "File"과 "Mole(두더지)"의 합성어입니다. 이 이름에는 다음과 같은 의미가 담겨 있습니다:

1. **파일 변경 감지**: 두더지가 땅 위로 고개를 내미는 것처럼, FileMole은 파일 시스템의 변화를 감지하고 이를 사용자에게 알립니다. 변화된 정보에 대해 더 많을 정보를 제공합니다.

2. **은밀한 작업**: 두더지가 땅 속에서 보이지 않게 작업하듯이, FileMole은 백그라운드에서 조용히 파일 시스템을 모니터링합니다.

3. **연결과 동기화**: 두더지가 터널을 파서 여러 지점을 연결하듯이, FileMole은 다양한 저장소(로컬, 원격, 클라우드) 간의 파일 동기화를 가능하게 합니다.

4. **적응성**: 두더지가 다양한 환경에 적응하듯이, FileMole은 여러 종류의 파일 시스템과 저장소에 적응하여 작동합니다.

## 기능
- 로컬 파일시스템, 원격 스토리지, 클라우드 파일 서비스에 대해서 디렉토리 목록, 파일 목록을 제공합니다.
- 파일 이름과 경로를 색인하여 보다 빠른 탐색 기능을 제공 합니다.
- 로컬 디스크로 동기화 기능을 제공합니다. (아직 구현 안됨)
- 파일, 폴더에 관한 작업 (생성, 이름 변경, 수정, 삭제 등) 이벤트를 제공합니다.
- Text, ODF, PDF 파일의 경우 텍스트 기준으로 변경 내용을 DIFF와 함게 제공합니다. (아직 구현 안됨)
- 파일 특성 및 캐시, 컨텐츠 해시 등 다양한 기능과 옵션을 통해 이벤트 디바운스 기능을 제공합니다.
- 파일 버전관리를 자동화 할 수 있습니다. (내부적으로 git을 사용합니다.) (아직 구현 안됨)
- FileChanged 이벤트가 빈번하게 발생하지 않도록 파일 hash를 검사해서 실제 파일의 내용이 변경되었을 경우에만 이벤트가 호출됩니다.
  - FileChaned 이벤트를 최소화 하기 위한 목적
  - 바뀐사실만 전달하는것이 아니고 바뀐 내용까지 전달해줌 (git, diff 기술)
- P2P 방식으로 파일 공유

## 주요 파일 및 역할

| 파일명 | 역할 |
|--------|------|
| `Events\FileSystemEvent.cs` | 파일 시스템 이벤트를 나타내는 내부 클래스, 파일 생성, 변경, 삭제, 이름 변경 이벤트에 사용됨. |
| `Events\FMFileSystemWatcher.cs` | 파일 시스템 감시자 클래스, 지정된 디렉토리에서 파일 이벤트를 감시하고 핸들러 메서드를 호출함. |
| `Indexing\FileIndexer.cs` | 파일 인덱서를 구현하는 클래스, 파일 정보를 데이터베이스에 저장하고 파일의 변경 여부를 검사함. |
| `Services\CloudFileService.cs` | 클라우드 파일 서비스의 기본 구조를 정의하는 클래스 (구현되지 않음). |
| `Services\IFileSystemService.cs` | 파일 시스템 서비스 인터페이스 (구현되지 않음). |
| `Services\LocalFileSystemService.cs` | 로컬 파일 시스템 서비스를 위한 클래스 (구현되지 않음). |
| `Services\RemoteStorageService.cs` | 원격 스토리지 서비스를 위한 클래스 (구현되지 않음). |
| `Storage\GoogleDriveStorageProvider.cs` | Google Drive 클라우드 스토리지 제공자를 위한 클래스 (메서드가 아직 구현되지 않음). |
| `Storage\IStorageProvider.cs` | 다양한 저장소 제공자(Google Drive, OneDrive 등)를 위한 인터페이스 정의. |
| `Storage\LocalStorageProvider.cs` | 로컬 파일 시스템에 대한 저장소 제공자 클래스, 파일과 디렉토리 작업을 구현함. |
| `Storage\OneDriveStorageProvider.cs` | OneDrive 클라우드 스토리지 제공자를 위한 클래스 (메서드가 아직 구현되지 않음). |
| `Storage\RemoteStorageProvider.cs` | 원격 스토리지 제공자를 위한 클래스 (메서드가 아직 구현되지 않음). |
| `Utils\HashGenerator.cs` | 파일의 해시를 생성하는 유틸리티 클래스. |
| `Utils\IgnoreManager.cs` | 파일 및 디렉토리를 무시할 패턴을 관리하는 유틸리티 클래스. |
| `Debouncer.cs` | 디바운서 클래스로, 특정 작업의 빈도를 제한하는 데 사용됨. |
| `FileMole.cs` | FileMole 라이브러리의 핵심 클래스, 파일 시스템 이벤트 처리 및 파일 인덱싱을 관리함. |
| `FileMoleBuilder.cs` | FileMole의 설정 및 인스턴스를 구성하는 빌더 클래스. |
| `FileMoleEventArgs.cs` | 파일 이벤트 관련 인수 클래스로, 이벤트 핸들러에 전달되는 데이터 구조를 정의함. |
| `FileMoleOptions.cs` | FileMole의 옵션을 설정하는 클래스. 파일 경로, 타입, 프로바이더 등을 정의함. |
| `FMDirectoryInfo.cs` | 디렉토리 정보를 나타내는 클래스, 디렉토리 관련 작업을 제공함. |
| `FMFileInfo.cs` | 파일 정보를 나타내는 클래스, 파일 관련 작업을 제공함. |
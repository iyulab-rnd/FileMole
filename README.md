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
- 로컬 디스크로 동기화 기능을 제공합니다.
- 파일, 폴더에 관한 작업 (생성, 이름 변경, 수정, 삭제 등) 이벤트를 제공합니다.
- Text, ODF, PDF 파일의 경우 텍스트 기준으로 변경 내용을 DIFF와 함게 제공합니다.
- 파일 특성 및 캐시, 컨텐츠 해시 등 다양한 기능과 옵션을 통해 이벤트 디바운스 기능을 제공합니다.
- 파일 버전관리를 자동화 할 수 있습니다. (내부적으로 git을 사용합니다.)

## 주요 파일 및 역할

| 파일명 | 역할 |
|--------|------|
| Core/IFileMole.cs | FileMole의 주요 기능을 정의하는 인터페이스 |
| Core/FileMole.cs | FileMole의 주요 기능을 구현하는 클래스 |
| Core/FileMoleBuilder.cs | FileMole 인스턴스를 구성하는 빌더 클래스 |
| Core/FileMoleOptions.cs | FileMole의 구성 옵션 클래스 |
| Storage/IStorageProvider.cs | 스토리지 프로바이더 인터페이스 |
| Storage/LocalStorageProvider.cs | 로컬 스토리지 구현 |
| Storage/RemoteStorageProvider.cs | 원격 스토리지 구현 |
| Storage/CloudStorageProvider.cs | 클라우드 스토리지 구현 |
| Storage/FMFileInfo.cs | 파일 정보 클래스 |
| Storage/FMDirectoryInfo.cs | 디렉토리 정보 클래스 |
| FileSystem/IFileSystemOperations.cs | 파일 시스템 작업 인터페이스 |
| FileSystem/FileSystemOperations.cs | 파일 시스템 작업 구현 |
| Events/FMFileSystemWatcher.cs | 파일 시스템 감시 구현 |
| Events/FileSystemEvent.cs | 파일 시스템 이벤트 클래스 |
| Indexing/FileIndexer.cs | 파일 인덱서 구현 |
| Services/CloudFileService.cs | 클라우드 파일 서비스 구현 |
| Services/IFileSystemService.cs | 파일 시스템 서비스 인터페이스 |
| Services/LocalFileSystemService.cs | 로컬 파일 시스템 서비스 구현 |
| Services/RemoteStorageService.cs | 원격 스토리지 서비스 구현 |
| Sync/ISyncService.cs | 동기화 서비스 인터페이스 |
| Sync/SyncService.cs | 동기화 서비스 구현 |
| Diff/IDiffGenerator.cs | 차이 생성기 인터페이스 |
| Diff/DiffGenerator.cs | 차이 생성기 구현 |
| VersionControl/IVersionControl.cs | 버전 관리 인터페이스 |
| VersionControl/GitVersionControl.cs | Git 기반 버전 관리 구현 |
| Utils/Debouncer.cs | 이벤트 디바운싱을 위한 유틸리티 클래스 |
| Utils/HashGenerator.cs | 파일 해시 생성 유틸리티 |
| Utils/FileTypeDetector.cs | 파일 유형 감지 유틸리티 |
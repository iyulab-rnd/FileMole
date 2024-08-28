# FileMole

FileMole은 파일 시스템을 손쉽게 다룰 수 있는 라이브러리 입니다.

## 지원
- .NET8, C#

## 기능
- 로컬 파일시스템, 원격 스토리지, 클라우드 파일 서비스에 대해서 디렉토리 목록, 파일 목록을 제공합니다.
- 파일 이름과 경로를 색인하여 보다 빠른 탐색 기능을 제공 합니다.
- 로컬 디스크로 동기화 기능을 제공합니다.
- 파일, 폴더에 관한 작업 (생성, 이름 변경, 수정, 삭제 등) 이벤트를 제공합니다.
- Text, ODF, PDF 파일의 경우 텍스트 기준으로 변경 내용을 DIFF와 함게 제공합니다.
- 파일 특성 및 캐시, 컨텐츠 해시 등 다양한 기능과 옵션을 통해 이벤트 디바운스 기능을 제공합니다.
- 내부적으로 git 을 사용하여 버전관리를 자동화 할 수 있습니다.

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
| Events/IFMFileSystemWatcher.cs | 파일 시스템 감시 인터페이스 |
| Events/FMFileSystemWatcher.cs | 파일 시스템 감시 구현 |
| Events/FileSystemEvent.cs | 파일 시스템 이벤트 클래스 |
| Indexing/IFileIndexer.cs | 파일 인덱서 인터페이스 |
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
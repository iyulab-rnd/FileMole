# FileMole Library Design Document

## 1. 개요

FileMole은 다양한 스토리지 시스템(로컬 디스크, 원격 서버, Azure, S3 등)을 위한 통합 파일 탐색기 라이브러리입니다. 
효율적인 파일 시스템 접근과 메타데이터 관리를 위해 SQLite 기반의 캐싱 시스템을 포함합니다.

## 2. 핵심 컴포넌트

### 2.1 Core Layer

#### FileSystemItem (기본 모델)
```csharp
public class FileSystemItem
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime LastAccessTime { get; set; }
    public DateTime LastWriteTime { get; set; }
    public string Extension { get; set; }
    public FileAttributes Attributes { get; set; }
    public IDictionary<string, object> Metadata { get; set; }
    public FileSecurityInfo Security { get; set; }
}
```

#### IStorageProvider (스토리지 인터페이스)
```csharp
public interface IStorageProvider
{
    string ProviderId { get; }
    Task<IEnumerable<FileSystemItem>> ListItemsAsync(string path);
    Task<FileSystemItem> GetItemAsync(string path);
    Task<Stream> OpenReadAsync(string path);
    Task<Stream> OpenWriteAsync(string path);
    Task DeleteAsync(string path, bool recursive = false);
    Task MoveAsync(string sourcePath, string destinationPath);
    Task CopyAsync(string sourcePath, string destinationPath);
    Task RenameAsync(string path, string newName);
}
```

### 2.2 Cache Layer

#### 데이터베이스 스키마

##### CachedItem 테이블
```sql
CREATE TABLE CachedItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProviderId TEXT NOT NULL,
    FullPath TEXT NOT NULL,
    Name TEXT NOT NULL,
    IsDirectory BOOLEAN NOT NULL,
    Size INTEGER NOT NULL,
    CreationTime DATETIME NOT NULL,
    LastAccessTime DATETIME NOT NULL,
    LastWriteTime DATETIME NOT NULL,
    CacheTime DATETIME NOT NULL,
    Extension TEXT,
    FileHash TEXT,
    UNIQUE(ProviderId, FullPath)
);
```

##### ItemAttribute 테이블
```sql
CREATE TABLE ItemAttributes (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL,
    Key TEXT NOT NULL,
    Value TEXT NOT NULL,
    FOREIGN KEY(ItemId) REFERENCES CachedItems(Id),
    UNIQUE(ItemId, Key)
);
```

##### ItemMetadata 테이블
```sql
CREATE TABLE ItemMetadata (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL,
    Key TEXT NOT NULL,
    Value TEXT NOT NULL,
    FOREIGN KEY(ItemId) REFERENCES CachedItems(Id),
    UNIQUE(ItemId, Key)
);
```

#### 캐시 인터페이스
```csharp
public interface IFileSystemCache
{
    Task<FileSystemItem> GetCachedItemAsync(string providerId, string path);
    Task CacheItemAsync(string providerId, string path, FileSystemItem item);
    Task InvalidateCacheAsync(string providerId, string path);
    Task<IEnumerable<FileSystemItem>> SearchCacheAsync(string providerId, string searchPattern, SearchOptions options);
}
```

### 2.3 File System Monitoring

#### FileSystemMonitor 서비스
```csharp
public class FileSystemMonitor : IHostedService
{
    private readonly CacheDbContext _dbContext;
    private readonly Dictionary<string, FileSystemWatcher> _watchers;
    
    public async Task StartMonitoringAsync(string providerId, string path)
    {
        var watcher = new FileSystemWatcher(path);
        watcher.Created += OnFileSystemItemChanged;
        watcher.Changed += OnFileSystemItemChanged;
        watcher.Deleted += OnFileSystemItemDeleted;
        watcher.Renamed += OnFileSystemItemRenamed;
        
        watcher.EnableRaisingEvents = true;
        _watchers[path] = watcher;
    }
    
    private async void OnFileSystemItemChanged(object sender, FileSystemEventArgs e)
    {
        var item = await GetFileSystemItemInfo(e.FullPath);
        await UpdateCacheAsync(item);
    }
}
```

## 3. 주요 기능

### 3.1 파일 시스템 작업
- 파일/폴더 목록 조회
- 파일/폴더 생성, 삭제, 이동
- 메타데이터 조회/수정
- 권한 관리

### 3.2 캐싱 시스템
- SQLite 기반 메타데이터 캐시
- 계층적 캐시 구조
- 캐시 만료 관리
- 검색 기능

### 3.3 변경 감지 시스템
- 실시간 파일 시스템 모니터링
- 자동 캐시 업데이트
- 이벤트 기반 동기화

## 4. 사용 예시

### 4.1 기본 사용
```csharp
var client = new FileMoleClient();
await client.UseLocalFileSystem("C:\\");

// 파일 목록 조회
var files = await client.ListItemsAsync("local", "/documents");
```

### 4.2 캐시 사용
```csharp
var cache = new SqliteFileSystemCache(new CacheOptions
{
    CacheExpiration = TimeSpan.FromMinutes(10)
});

// 캐시된 항목 검색
var results = await cache.SearchCacheAsync("local", "*.pdf", new SearchOptions
{
    LastModifiedAfter = DateTime.Now.AddDays(-7)
});
```

### 4.3 파일 시스템 모니터링
```csharp
var monitor = new FileSystemMonitor(cache);
await monitor.StartMonitoringAsync("local", "C:\\Documents");

monitor.FileChanged += async (sender, e) =>
{
    Console.WriteLine($"File changed: {e.FullPath}");
    await cache.InvalidateCacheAsync("local", e.FullPath);
};
```

## 5. 성능 고려사항

### 5.1 캐시 최적화
- 인덱스 설정
- 캐시 크기 제한
- 만료 정책

### 5.2 모니터링 최적화
- 이벤트 디바운싱
- 배치 업데이트
- 변경 필터링

## 7. 프로젝트 구조


```
FileMole/
├── FileMole/                           # 메인 라이브러리 프로젝트
│   ├── Core/
│   │   ├── Models/
│   │   │   ├── FileSystemItem.cs
│   │   │   ├── FileSecurityInfo.cs
│   │   │   ├── ApiResponse.cs
│   │   │   └── DriveInfo.cs
│   │   ├── Interfaces/
│   │   │   ├── IStorageProvider.cs
│   │   │   ├── IFileSystemCache.cs
│   │   │   └── IFileSystemMonitor.cs
│   │   └── Exceptions/
│   │       ├── FileMoleException.cs
│   │       └── ProviderNotFoundException.cs
│   │
│   ├── Cache/
│   │   ├── CacheDbContext.cs
│   │   ├── CachedItem.cs
│   │   └── SqliteFileSystemCache.cs
│   │
│   ├── Monitoring/
│   │   ├── FileSystemMonitor.cs
│   │   └── ChangeNotificationService.cs
│   │
│   ├── Providers/
│   │   ├── LocalFileSystemProvider.cs
│   │   ├── RemoteFileSystemProvider.cs
│   │   ├── AzureStorageProvider.cs
│   │   └── S3StorageProvider.cs
│   │
│   ├── Server/
│   │   ├── FileMoleServer.cs
│   │   └── Controllers/
│   │       └── FileSystemController.cs
│   │
│   ├── Client/
│   │   └── FileMoleClientApp.cs
│   │
│   └── Common/
│       ├── FileMoleManager.cs
│       └── Utils/
│           ├── PathUtils.cs
│           └── SecurityUtils.cs
│
├── FileMole.Tests/                     # 테스트 프로젝트
│   ├── Core/
│   ├── Cache/
│   ├── Providers/
│   └── Integration/
│
└── FileMole.Sample/                    # 샘플 프로젝트
    ├── Server/
    │   └── Program.cs
    └── Client/
        └── Program.cs
```
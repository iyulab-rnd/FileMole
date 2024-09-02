using FileMoles;
using FileMoles.Storage;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace FileMoles.Samples.WpfApp;

public partial class MainWindow : Window
{
    private FileMole _fileMole;
    private ObservableCollection<FolderItem> _rootFolders;
    private ObservableCollection<FMFileInfo> _currentFiles;
    private string _currentPath;

    public MainWindow()
    {
        InitializeComponent();
        InitializeFileMole();
        LoadDrives();
        InitializeEventHandlers();
    }

    private void InitializeFileMole()
    {
        var builder = new FileMoleBuilder();

        foreach (var drive in DriveInfo.GetDrives())
        {
            builder.AddMole(drive.Name, MoleType.Local);
        }

        _fileMole = builder.Build();

        _fileMole.FileCreated += FileMole_FileCreated;
        _fileMole.FileChanged += FileMole_FileChanged;
        _fileMole.FileDeleted += FileMole_FileDeleted;
        _fileMole.FileRenamed += FileMole_FileRenamed;
        _fileMole.InitialScanCompleted += (sender, e) => LogMessage("Initial scan completed.");
        _fileMole.MoleTrackChanged += (sender, e) => LogMessage($"MoleTrackChanged: {e.FullPath}, {e.Diff}");

        LogMessage("FileMole initialized.");
    }

    private void LoadDrives()
    {
        _rootFolders = new ObservableCollection<FolderItem>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            _rootFolders.Add(new FolderItem { Name = drive.Name, Path = drive.Name });
        }
        folderTreeView.ItemsSource = _rootFolders;
        LogMessage("Drives loaded.");
    }

    private void InitializeEventHandlers()
    {
        folderTreeView.SelectedItemChanged += FolderTreeView_SelectedItemChanged;
        LogMessage("Event handlers initialized.");
    }

    private async void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderItem selectedFolder)
        {
            _currentPath = selectedFolder.Path;
            await UpdateFileListView(selectedFolder.Path);
            await LoadSubFolders(selectedFolder);
            LogMessage($"Folder selected: {selectedFolder.Path}");
        }
    }

    private async Task UpdateFileListView(string folderPath)
    {
        try
        {
            var files = await _fileMole.GetFilesAsync(folderPath);
            _currentFiles = new ObservableCollection<FMFileInfo>(files);
            fileListView.ItemsSource = _currentFiles;
            LogMessage($"File list updated for: {folderPath}");
        }
        catch (Exception ex)
        {
            LogMessage($"Error loading files: {ex.Message}");
            MessageBox.Show($"Error loading files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadSubFolders(FolderItem parentFolder)
    {
        if (parentFolder.SubFolders.Count == 0)
        {
            try
            {
                var subDirectories = await _fileMole.GetDirectoriesAsync(parentFolder.Path);
                foreach (var dir in subDirectories)
                {
                    parentFolder.SubFolders.Add(new FolderItem { Name = dir.Name, Path = dir.FullPath });
                }
                LogMessage($"Subfolders loaded for: {parentFolder.Path}");
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading subfolders: {ex.Message}");
                MessageBox.Show($"Error loading subfolders: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void FileMole_FileCreated(object sender, FileMoleEventArgs e)
    {
        Dispatcher.Invoke(async () =>
        {
            LogMessage($"File created: {e.FullPath}");
            if (Path.GetDirectoryName(e.FullPath) == _currentPath)
            {
                await HandleFileCreated(e);
            }
            await UpdateFolderStructure(e);
        });
    }

    private void FileMole_FileChanged(object sender, FileMoleEventArgs e)
    {
        Dispatcher.Invoke(async () =>
        {
            LogMessage($"File changed: {e.FullPath}");
            if (Path.GetDirectoryName(e.FullPath) == _currentPath)
            {
                await HandleFileChanged(e);
            }
        });
    }

    private void FileMole_FileDeleted(object sender, FileMoleEventArgs e)
    {
        Dispatcher.Invoke(async () =>
        {
            LogMessage($"File deleted: {e.FullPath}");
            if (Path.GetDirectoryName(e.FullPath) == _currentPath)
            {
                HandleFileDeleted(e);
            }
            await UpdateFolderStructure(e);
        });
    }

    private void FileMole_FileRenamed(object sender, FileMoleEventArgs e)
    {
        Dispatcher.Invoke(async () =>
        {
            LogMessage($"File renamed: {e.OldFullPath} to {e.FullPath}");
            if (Path.GetDirectoryName(e.FullPath) == _currentPath || Path.GetDirectoryName(e.OldFullPath) == _currentPath)
            {
                await HandleFileRenamed(e);
            }
            await UpdateFolderStructure(e);
        });
    }

    private async Task HandleFileCreated(FileMoleEventArgs e)
    {
        if (!e.IsDirectory)
        {
            var newFile = await _fileMole.GetFileAsync(e.FullPath);
            if (newFile != null)
            {
                _currentFiles.Add(newFile);
                LogMessage($"File added to list: {newFile.Name}");
            }
        }
    }

    private async Task HandleFileChanged(FileMoleEventArgs e)
    {
        if (!e.IsDirectory)
        {
            var changedFile = await _fileMole.GetFileAsync(e.FullPath);
            if (changedFile != null)
            {
                var index = _currentFiles.IndexOf(_currentFiles.FirstOrDefault(f => f.FullPath == e.FullPath));
                if (index != -1)
                {
                    _currentFiles[index] = changedFile;
                    LogMessage($"File updated in list: {changedFile.Name}");
                }
            }
        }
    }

    private void HandleFileDeleted(FileMoleEventArgs e)
    {
        var fileToRemove = _currentFiles.FirstOrDefault(f => f.FullPath == e.FullPath);
        if (fileToRemove != null)
        {
            _currentFiles.Remove(fileToRemove);
            LogMessage($"File removed from list: {fileToRemove.Name}");
        }
    }

    private async Task HandleFileRenamed(FileMoleEventArgs e)
    {
        try
        {
            var oldFile = _currentFiles.FirstOrDefault(f => f.FullPath == e.OldFullPath);
            if (oldFile != null)
            {
                _currentFiles.Remove(oldFile);
                LogMessage($"Old file removed from list: {oldFile.Name}");
            }

            var newFile = await _fileMole.GetFileAsync(e.FullPath);
            if (newFile != null)
            {
                _currentFiles.Add(newFile);
                LogMessage($"New file added to list: {newFile.Name}");
            }
            else
            {
                LogMessage($"Warning: Could not get information for renamed file: {e.FullPath}");
            }

            // 리스트 뷰 업데이트를 강제로 수행
            fileListView.Items.Refresh();
        }
        catch (Exception ex)
        {
            LogMessage($"Error handling renamed file: {ex.Message}");
        }
    }


    private async Task UpdateFolderStructure(FileMoleEventArgs e)
    {
        var parentPath = Path.GetDirectoryName(e.FullPath);
        var parentFolder = FindFolderItem(parentPath);

        if (parentFolder != null)
        {
            switch (e.ChangeType)
            {
                case WatcherChangeTypes.Created:
                    if (e.IsDirectory)
                    {
                        parentFolder.SubFolders.Add(new FolderItem { Name = Path.GetFileName(e.FullPath), Path = e.FullPath });
                        LogMessage($"Folder added to structure: {e.FullPath}");
                    }
                    break;
                case WatcherChangeTypes.Deleted:
                    var folderToRemove = parentFolder.SubFolders.FirstOrDefault(f => f.Path == e.FullPath);
                    if (folderToRemove != null)
                    {
                        parentFolder.SubFolders.Remove(folderToRemove);
                        LogMessage($"Folder removed from structure: {e.FullPath}");
                    }
                    break;
                case WatcherChangeTypes.Renamed:
                    if (e.IsDirectory)
                    {
                        var oldFolder = parentFolder.SubFolders.FirstOrDefault(f => f.Path == e.OldFullPath);
                        if (oldFolder != null)
                        {
                            oldFolder.Name = Path.GetFileName(e.FullPath);
                            oldFolder.Path = e.FullPath;
                            LogMessage($"Folder renamed in structure: {e.OldFullPath} to {e.FullPath}");
                        }
                    }
                    break;
            }
        }
    }

    private FolderItem FindFolderItem(string path)
    {
        foreach (var rootFolder in _rootFolders)
        {
            var result = FindFolderItemRecursive(rootFolder, path);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private FolderItem FindFolderItemRecursive(FolderItem folder, string path)
    {
        if (folder.Path == path)
        {
            return folder;
        }

        foreach (var subFolder in folder.SubFolders)
        {
            var result = FindFolderItemRecursive(subFolder, path);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private void LogMessage(string message)
    {
        Dispatcher.Invoke(() =>
        {
            logTextBox.AppendText($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}");
            logTextBox.ScrollToEnd();
        });
    }
}
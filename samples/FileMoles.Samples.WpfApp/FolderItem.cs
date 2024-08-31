using System.Collections.ObjectModel;

namespace FileMoles.Samples.WpfApp
{
    public class FolderItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public ObservableCollection<FolderItem> SubFolders { get; set; }

        public FolderItem()
        {
            SubFolders = new ObservableCollection<FolderItem>();
        }
    }
}
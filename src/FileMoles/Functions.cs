using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileMoles
{
    internal static class Functions
    {
        internal static string GetFileMoleDataPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileMole");
        }

        internal static string GetDatabasePath()
        {
            return Path.Combine(GetFileMoleDataPath(), "filemole.db");
        }
    }
}

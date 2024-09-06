using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileMoles
{
    public class Logger
    {
        internal static void WriteLine(string message)
        {
            Debug.WriteLine(message);
        }
    }
}

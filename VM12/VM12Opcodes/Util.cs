using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Util
{
    public static class Util
    {
        public static IEnumerable<FileInfo> GetFilesByExtensions(this DirectoryInfo dir, params string[] extensions)
        {
            if (extensions == null)
                throw new ArgumentNullException("extensions");
            IEnumerable<FileInfo> files = dir.EnumerateFiles("*", SearchOption.AllDirectories);
            return files.Where(f => extensions.Contains(f.Extension));
        }

        public static int CountLines(this string str)
        {
            int count = 0;
            int position = 0;
            while ((position = str.IndexOf('\n', position)) != -1)
            {
                count++;
                position++;
            }
            return count;
        }
    }
}

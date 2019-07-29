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

        public static int CountLines(this StringBuilder sb)
        {
            int lines = 0;
            for (int i = 0; i < sb.Length; i++)
            {
                if (sb[i] == '\n') lines++;
            }
            return lines;
        }
        
        public static bool IsOnly(this string str, int startat, char[] letters)
        {
            for (int i = startat; i < str.Length; i++)
            {
                if (letters.Contains(str[i]) == false) return false;
            }

            return true;
        }

    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace VM12Util
{
    public static class Util
    {
        public static Dictionary<K, V> ToDictionaryGood<K, V>(this List<V> values, Func<V, K> keyFunc)
        {
            Dictionary<K, V> dict = new Dictionary<K, V>(values.Count * 2);
            foreach (var value in values)
            {
                K key = keyFunc(value);
                if (dict.ContainsKey(key)) throw new ArgumentException($"The key {key} already exists in the dictionary!");
                dict.Add(key, value);
            }
            return dict;
        }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GetMS(this Stopwatch watch)
        {
            return (watch.ElapsedTicks / (double)Stopwatch.Frequency) * 1000d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GetSec(this Stopwatch watch)
        {
            return watch.ElapsedTicks / (double)Stopwatch.Frequency;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double GetMsFromTicks(long ticks)
        {
            return (ticks / (double)Stopwatch.Frequency) * 1000d;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Log2(int v)
        {
            int r = 0xFFFF - v >> 31 & 0x10;
            v >>= r;
            int shift = 0xFF - v >> 31 & 0x8;
            v >>= shift;
            r |= shift;
            shift = 0xF - v >> 31 & 0x4;
            v >>= shift;
            r |= shift;
            shift = 0x3 - v >> 31 & 0x2;
            v >>= shift;
            r |= shift;
            r |= (v >> 1);
            return r;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int HexToInt(char c)
        {
            c = char.ToUpper(c);  // may not be necessary

            return c < 'A' ? c - '0' : 10 + (c - 'A');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int OctalToInt(char c)
        {
            return c - '0';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BinaryToInt(char c)
        {
            return c - '0';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsHexOrUnderscore(char c)
        {
            return (c >= '0' && c <= '9') ||
                   (c >= 'a' && c <= 'f') ||
                   (c >= 'A' && c <= 'F') ||
                   c == '_';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsOctalOrUnderscore(char c)
        {
            return (c >= '0' && c <= '7') || c == '_';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsBinaryOrUnderscore(char c)
        {
            return c == '0' || c == '1' || c == '_';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDecimalOrUnderscore(char c)
        {
            return char.IsNumber(c) || c == '_';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StringRef ToRef(this string str) => new StringRef(str, 0, str.Length);
    }
}

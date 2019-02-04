using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace VM12
{
    static class Utils
    {
        /// <summary>
        /// Returns the index of the first element in the sequence 
        /// that satisfies a condition.
        /// </summary>
        /// <typeparam name="TSource">
        /// The type of the elements of <paramref name="source"/>.
        /// </typeparam>
        /// <param name="source">
        /// An <see cref="IEnumerable{T}"/> that contains
        /// the elements to apply the predicate to.
        /// </param>
        /// <param name="predicate">
        /// A function to test each element for a condition.
        /// </param>
        /// <returns>
        /// The zero-based index position of the first element of <paramref name="source"/>
        /// for which <paramref name="predicate"/> returns <see langword="true"/>;
        /// or -1 if <paramref name="source"/> is empty
        /// or no element satisfies the condition.
        /// </returns>
        public static int IndexOf<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            int i = 0;

            foreach (TSource element in source)
            {
                if (predicate(element))
                    return i;

                i++;
            }

            return -1;
        }

        public static string ReplaceEnd(this string value, string toReplace, string replacement)
        {
            if (value.EndsWith(toReplace))
            {
                return value.Substring(0, value.Length - toReplace.Length) + replacement;
            }
            else
            {
                return value;
            }
        }

        public static T[] SubArray<T>(this T[] source, int offset, int length)
        {
            T[] sub = new T[length];
            Array.Copy(source, offset, sub, 0, length);
            return sub;
        }

        public static bool TryParseHex(string str, out int value)
        {
            if (str.StartsWith("0x"))
            {
                str = str.Substring(2);
            }

            return int.TryParse(str, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryParseNumber(string str, out int value)
        {
            if (str.StartsWith("0x"))
            {
                return int.TryParse(str.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }
            else if (str.StartsWith("0b"))
            {
                try
                {
                    value = Convert.ToInt32(str.Substring(2), 2);
                    return true;
                }
                catch
                {
                    value = default;
                    return false;
                }
            }
            else
            {
                return int.TryParse(str, out value);
            }
        }

        [DllImport("msvcrt.dll", EntryPoint = "memcpy", CallingConvention = CallingConvention.Cdecl, SetLastError = false)]
        public static extern IntPtr MemCopy(IntPtr dest, IntPtr src, uint count);

        public static unsafe void MemSet(int* data, int value, int count)
        {
            int block_size = 32;
            int length = Math.Min(block_size, count);
            
            int index = 0;
            while (index < length)
            {
                data[index++] = value;
            }

            while (index < count)
            {
                MemCopy((IntPtr)data + (index * sizeof(int)), (IntPtr)data, (uint)Math.Min(block_size, (count - index)) * sizeof(int));
                index += block_size;
                block_size *= 2;
            }
        }

        public static int Clamp(this int value, int min, int max)
        {
            return value > max ? max : value < min ? min : value;
        }

        public static float Clamp(this float value, float min, float max)
        {
            return value > max ? max : value < min ? min : value;
        }

        public static double Clamp(this double value, double min, double max)
        {
            return value > max ? max : value < min ? min : value;
        }

    }
}

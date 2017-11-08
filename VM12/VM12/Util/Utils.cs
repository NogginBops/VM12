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
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace VM12Util
{
    public struct StringRef : IEquatable<StringRef>, IEnumerable<char>
    {
        public string Data;
        public int Index;
        public int Length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StringRef(string data, int index, int length)
        {
            Data = data;
            Index = index;
            Length = length;
        }

        public bool StartsWith(string str)
        {
            if (str.Length > Length) return false;

            for (int i = 0; i < str.Length; i++)
            {
                if (Data[Index + i] != str[i]) return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StringRef Substring(int start)
        {
            if (start > Length) throw new ArgumentException("The start is past the end of the string!");
            return new StringRef(Data, Index + start, Length - start);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StringRef Substring(int start, int length)
        {
            if (length > Length - start) throw new ArgumentException("The start and length provieded exceed the length of the string!");
            return new StringRef(Data, Index + start, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => Data?.Substring(Index, Length);

        public override bool Equals(object obj)
        {
            return obj is StringRef @ref && Equals(@ref);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(StringRef other)
        {
            if (Length != other.Length) return false;
            for (int i = 0; i < Length; i++)
            {
                if (Data[Index + i] != other.Data[other.Index + i]) return false;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            var hashCode = 596452045;
            for (int i = 0; i < Length; i++)
            {
                hashCode = hashCode * -1521134295 + Data[Index + i].GetHashCode();
            }
            hashCode = hashCode * -1521134295 + Length.GetHashCode();
            return hashCode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(StringRef left, StringRef right)
        {
            return left.Equals(right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(StringRef left, StringRef right)
        {
            return !(left == right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator StringRef(string str) => new StringRef(str, 0, str.Length);

        [Obsolete]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator string(StringRef strRef) => strRef.ToString();

        public IEnumerator<char> GetEnumerator()
        {
            for (int i = 0; i < Length; i++)
            {
                yield return this[i];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public char this[int i] => Data[Index + i];
    }
}

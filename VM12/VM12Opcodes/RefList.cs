using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace VM12Util
{
    public class RefList<T>
    {
        public T[] Array;
        public int Elements;

        public int Count => Elements;

        public RefList(int size = 16)
        {
            Array = new T[size];
        }

        private void EnsureCapacity(int cap)
        {
            if (cap > Array.Length)
                Resize(cap);
        }

        private void Resize(int cap)
        {
            int newSize = Array.Length + (Array.Length / 2);
            if (newSize < cap) newSize = cap;
            System.Array.Resize(ref Array, newSize);
        }

        public ref T Add()
        {
            EnsureCapacity(Elements + 1);
            return ref Array[Elements++];
        }

        public T this[int index] => index < Elements ? Array[index] : throw new ArgumentOutOfRangeException(nameof(index));

        public RefQueue<T> FlipToQueue() => new RefQueue<T>(Array, Elements);
    }

    public class RefQueue<T>
    {
        public T[] Array;
        public int Elements;
        public int Count => Elements - Index;
        public int Index;

        public RefQueue(T[] array, int elements)
        {
            Array = array;
            Elements = elements;
            Index = 0;
        }

        public ref T Dequeue()
        {
            if (Index >= Array.Length) throw new InvalidOperationException("There are no more elements in the queue");
            return ref Array[Index++];
        }

        public ref T Peek()
        {
            if (Index >= Array.Length) throw new InvalidOperationException("There are no more elements in the queue");
            return ref Array[Index];
        }

        public ref T Peek(int i)
        {
            if (Index + i >= Array.Length) throw new InvalidOperationException($"There are not enough elements to peek {i} elements ahead");
            return ref Array[Index + i];
        }

        public void Revert(int elements)
        {
            if (Index - elements < 0) throw new InvalidOperationException("Cannot revert past the start!");
            Index -= elements;
        }
    }
}

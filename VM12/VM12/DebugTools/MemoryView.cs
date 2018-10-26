using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.ComponentModel.Design;
using Be.Windows.Forms;

namespace Debugging
{
    using VM12 = VM12.VM12;

    public partial class MemoryView : UserControl
    {
        // NOTE: Maybe move to another file
        private class VM12MemoryProvider : IByteProvider
        {
            private bool hasChanges = false;

            public VM12 VM12 { get; }
            
            private int startAddress;
            private long length;
            
            public long Length => length * 2;

            public event EventHandler LengthChanged;
            public event EventHandler Changed;

            public VM12MemoryProvider(VM12 vm12)
            {
                this.VM12 = vm12;
                length = VM12.MEM.Length;
            }

            public void SetStartAddress(int startAddress)
            {
                this.startAddress = startAddress;
                OnChanged(EventArgs.Empty);
            }

            public void SetLength(long length)
            {
                this.length = Math.Min(length, VM12.MEM.Length - startAddress);
                OnLengthChanged(EventArgs.Empty);
            }

            public void SetStartAndLength(int startAddress, long length)
            {
                bool lengthChanged = this.length != length;

                this.startAddress = startAddress;
                this.length = length;

                if (lengthChanged) OnLengthChanged(EventArgs.Empty);

                OnChanged(EventArgs.Empty);
            }

            public void Update()
            {
                OnChanged(EventArgs.Empty);
            }

            private void OnChanged(EventArgs e)
            {
                hasChanges = true;
                this.Changed?.Invoke(this, e);
            }

            private void OnLengthChanged(EventArgs e)
            {
                hasChanges = true;
                this.LengthChanged?.Invoke(this, e);
            }

            public void ApplyChanges()
            {
                hasChanges = false;
            }

            public void DeleteBytes(long index, long length)
            {
                throw new NotSupportedException();
            }

            public bool HasChanges()
            {
                return hasChanges;
            }

            public void InsertBytes(long index, byte[] bs)
            {
                throw new NotSupportedException();
            }

            public byte ReadByte(long index)
            {
                // Figure out if it is the upper or lower byte
                if (index % 2 == 0)
                {
                    // This is if the byte is the higher one
                    return (byte)(VM12.MEM[startAddress + (index / 2)] >> 8);
                }
                else
                {
                    // This is if the byte is the lower one
                    return (byte)(VM12.MEM[startAddress + (index / 2)] & 0xFF);
                }
            }

            public bool SupportsDeleteBytes()
            {
                return false;
            }

            public bool SupportsInsertBytes()
            {
                return false;
            }

            public bool SupportsWriteByte()
            {
                return true;
            }

            public void WriteByte(long index, byte value)
            {
                // Figure out if it is the upper or lower byte
                if (index % 2 == 0)
                {
                    // This is if the byte is the higher one
                    VM12.MEM[startAddress + (index / 2)] = value << 8 | (0xFF & VM12.MEM[startAddress + ((index / 2) + 1)]);
                }
                else
                {
                    // This is if the byte is the lower one
                    VM12.MEM[startAddress + (index / 2)] = VM12.MEM[startAddress + ((index / 2) - 1)] << 8 | value;
                }
                OnChanged(EventArgs.Empty);
            }
        }

        private VM12MemoryProvider MemoryProvider;

        public MemoryView()
        {
            InitializeComponent();
            
            byte[] data = new byte[10000];

            new Random().NextBytes(data);
            
            hexBox.ByteProvider = new DynamicByteProvider(data);
        }

        internal void SetVM12(VM12 vm12)
        {
            MemoryProvider = new VM12MemoryProvider(vm12);
            hexBox.ByteProvider = MemoryProvider;
        }

        public void SetStartAddress(int addr)
        {
            MemoryProvider?.SetStartAddress(addr);
            hexBox.Invalidate();
        }

        public void SetLength(int length)
        {
            MemoryProvider?.SetLength(length);
            hexBox.Invalidate();
        }

        public void SetStartAndLength(int start, int length)
        {
            MemoryProvider?.SetStartAndLength(start, length);
            hexBox.Invalidate();
        }

        public void UpdateView()
        {
            MemoryProvider?.Update();
            hexBox.Invalidate();
        }
    }
}

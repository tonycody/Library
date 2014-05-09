using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Library.Collections
{
    public unsafe class BinaryArray : ManagerBase
    {
        private int _length;

        private byte[] _buffer;

        private GCHandle _h_buffer;
        private byte* _p_buffer;

        private volatile bool _disposed;

        public BinaryArray(int length)
        {
            _length = length;

            _buffer = new byte[(_length + (8 - 1)) / 8];

            _h_buffer = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            _p_buffer = (byte*)_h_buffer.AddrOfPinnedObject();
        }

        public int Length
        {
            get
            {
                return _length;
            }
        }

        public void Set(int index, bool value)
        {
            if (index < 0 || index >= _length) throw new ArgumentOutOfRangeException("index");

            if (value)
            {
                _p_buffer[index / 8] |= (byte)(0x80 >> (index % 8));
            }
            else
            {
                _p_buffer[index / 8] &= (byte)(~(0x80 >> (index % 8)));
            }
        }

        public bool Get(int index)
        {
            if (index < 0 || index >= _length) throw new ArgumentOutOfRangeException("index");

            return ((_p_buffer[index / 8] << (index % 8)) & 0x80) == 0x80;
        }

        public void Clear()
        {
            Unsafe.Zero(_buffer);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {

            }

            _h_buffer.Free();
        }
    }
}

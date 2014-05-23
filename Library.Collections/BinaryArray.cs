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
        private BufferManager _bufferManager;

        private byte[] _buffer;

        private volatile bool _disposed;

        public BinaryArray(int length, BufferManager bufferManager)
        {
            _length = length;
            _bufferManager = bufferManager;

            _buffer = _bufferManager.TakeBuffer((_length + (8 - 1)) / 8);
            Unsafe.Zero(_buffer);
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
                _buffer[index / 8] |= (byte)(0x80 >> (index % 8));
            }
            else
            {
                _buffer[index / 8] &= (byte)(~(0x80 >> (index % 8)));
            }
        }

        public bool Get(int index)
        {
            if (index < 0 || index >= _length) throw new ArgumentOutOfRangeException("index");

            return ((_buffer[index / 8] << (index % 8)) & 0x80) == 0x80;
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
                if (_buffer != null)
                {
                    try
                    {
                        _bufferManager.ReturnBuffer(_buffer);
                    }
                    catch (Exception)
                    {

                    }

                    _buffer = null;
                }
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;

namespace Library.Net.Amoeba
{
    class CacheManagerStreamReader : Stream
    {
        private KeyCollection _keys;
        private CacheManager _cacheManager;
        private BufferManager _bufferManager;

        private ArraySegment<byte> _blockBuffer;
        private int _blockBufferPosition;

        private int _keysIndex;

        private long _position;
        private long _length;

        private volatile bool _disposed;

        public CacheManagerStreamReader(KeyCollection keys, CacheManager cacheManager, BufferManager bufferManager)
        {
            _cacheManager = cacheManager;
            _keys = keys;
            _bufferManager = bufferManager;

            _blockBuffer = _cacheManager[_keys[_keysIndex]];
            _keysIndex++;

            _length = keys.Sum(n => (long)cacheManager.GetLength(n));
        }

        public override bool CanRead
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return true;
            }
        }

        public override bool CanWrite
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return false;
            }
        }

        public override bool CanSeek
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return false;
            }
        }

        public override long Position
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _position;
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                throw new NotSupportedException();
            }
        }

        public override long Length
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _length;
            }
        }

        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (offset < 0 || buffer.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || (buffer.Length - offset) < count) throw new ArgumentOutOfRangeException("count");

            count = (int)Math.Min(count, this.Length - this.Position);

            int readLength = 0;

            while (count > 0)
            {
                int length = Math.Min(count, _blockBuffer.Count - _blockBufferPosition);
                Unsafe.Copy(_blockBuffer.Array, _blockBuffer.Offset + _blockBufferPosition, buffer, offset, length);
                _blockBufferPosition += length;
                count -= length;
                offset += length;
                readLength += length;

                if (_blockBuffer.Count == _blockBufferPosition && _keysIndex < _keys.Count)
                {
                    _bufferManager.ReturnBuffer(_blockBuffer.Array);
                    _blockBuffer = _cacheManager[_keys[_keysIndex]];

                    _keysIndex++;
                    _blockBufferPosition = 0;
                }
            }

            _position += readLength;
            return readLength;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            throw new NotSupportedException();
        }

        public override void Flush()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
        }

        public override void Close()
        {
            if (_disposed) return;

            base.Close();
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (_disposed) return;
                _disposed = true;

                if (disposing)
                {
                    if (_blockBuffer.Array != null)
                    {
                        _bufferManager.ReturnBuffer(_blockBuffer.Array);
                        _blockBuffer = new ArraySegment<byte>();
                    }
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }
    }
}

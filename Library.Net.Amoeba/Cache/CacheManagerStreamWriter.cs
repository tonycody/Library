using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Library;

namespace Library.Net.Amoeba
{
    class CacheManagerStreamWriter : Stream
    {
        private CacheManager _cacheManager;
        private byte[] _blockBuffer;
        private int _blockBufferPosition = 0;
        private int _blockBufferLength = 0;
        private HashAlgorithm _hashAlgorithm;
        private BufferManager _bufferManager;

        private List<Key> _keyList = new List<Key>();
        private long _length;
        private bool _disposed = false;

        public CacheManagerStreamWriter(int blockLength, HashAlgorithm hashAlgorithm, CacheManager cacheManager, BufferManager bufferManager)
        {
            _hashAlgorithm = hashAlgorithm;
            _cacheManager = cacheManager;
            _bufferManager = bufferManager;
            _blockBuffer = bufferManager.TakeBuffer(blockLength);
            _blockBufferLength = blockLength;
        }

        public override bool CanRead
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return false;
            }
        }

        public override bool CanWrite
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return true;
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

                return _length;
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

            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (offset < 0 || buffer.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || (buffer.Length - offset) < count) throw new ArgumentOutOfRangeException("count");

            int writeLength = 0;

            while (count > 0)
            {
                int length = Math.Min(_blockBufferLength - _blockBufferPosition, count);
                Array.Copy(buffer, offset, _blockBuffer, _blockBufferPosition, length);
                _blockBufferPosition += length;
                offset += length;
                count -= length;
                writeLength += length;

                if (_blockBufferLength == _blockBufferPosition)
                {
                    var key = new Key();

                    if (_hashAlgorithm == HashAlgorithm.Sha512)
                    {
                        key.Hash = Sha512.ComputeHash(_blockBuffer, 0, _blockBufferPosition);
                        key.HashAlgorithm = _hashAlgorithm;
                    }
                    else
                    {
                        throw new NotSupportedException();
                    }

                    _cacheManager[key] = new ArraySegment<byte>(_blockBuffer, 0, _blockBufferPosition);
                    _keyList.Add(key);

                    _blockBufferPosition = 0;
                }
            }

            _length += writeLength;
        }

        public override void Flush()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
        }

        public override void Close()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (_blockBufferPosition != 0)
            {
                var key = new Key();

                if (_hashAlgorithm == HashAlgorithm.Sha512)
                {
                    key.Hash = Sha512.ComputeHash(_blockBuffer, 0, _blockBufferPosition);
                    key.HashAlgorithm = _hashAlgorithm;
                }
                else
                {
                    throw new NotSupportedException();
                }

                _cacheManager[key] = new ArraySegment<byte>(_blockBuffer, 0, _blockBufferPosition);
                _keyList.Add(key);

                _blockBufferPosition = 0;
            }
        }

        public IEnumerable<Key> GetKeys()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            foreach (var item in _keyList.Select(n => n.DeepClone()))
            {
                yield return item;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                try
                {
                    if (disposing)
                    {
                        if (_blockBuffer != null)
                        {
                            _bufferManager.ReturnBuffer(_blockBuffer);
                            _blockBuffer = null;
                        }

                        _disposed = true;
                    }
                }
                finally
                {
                    base.Dispose(disposing);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Library.Io
{
    public class CacheStream : Stream
    {
        private Stream _stream;
        private long _position;
        private int _bufferSize;
        private bool _leaveInnerStreamOpen;
        private bool _isReadMode = true;
        private BufferManager _bufferManager;

        private byte[] _readerBuffer;
        private System.Runtime.InteropServices.GCHandle _readerBufferGcHandle;
        private int _readerBufferPosition;
        private int _readerBufferLength;

        private byte[] _writerBlockBuffer;
        private System.Runtime.InteropServices.GCHandle _writerBufferGcHandle;
        private int _writerBufferPosition;

        private bool _disposed = false;

        public CacheStream(Stream stream, int bufferSize, bool leaveInnerStreamOpen, BufferManager bufferManager)
        {
            _stream = stream;

            try
            {
                _position = _stream.Position;
            }
            catch (Exception)
            {

            }

            _bufferSize = bufferSize;
            _leaveInnerStreamOpen = leaveInnerStreamOpen;
            _bufferManager = bufferManager;
        }

        public CacheStream(Stream stream, int bufferSize, BufferManager bufferManager)
            : this(stream, bufferSize, false, bufferManager)
        {

        }

        public override bool CanRead
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.CanRead;
            }
        }

        public override bool CanWrite
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.CanWrite;
            }
        }

        public override bool CanSeek
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.CanSeek;
            }
        }

        public override long Position
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(this.GetType().FullName);

                return _position;
            }
            set
            {
                if (_disposed)
                    throw new ObjectDisposedException(this.GetType().FullName);

                if (_readerBuffer != null)
                {
                    if (_position < value)
                    {
                        int s = (int)(value - _position);

                        _readerBufferPosition += s;

                        if (_readerBufferPosition >= _readerBufferLength)
                        {
                            if (!_stream.CanSeek)
                                throw new NotSupportedException();

                            _readerBufferPosition = 0;
                            _readerBufferLength = 0;

                            _stream.Position = value;
                        }
                    }
                    else
                    {
                        int s = (int)(_position - value);

                        _readerBufferPosition -= s;

                        if (_readerBufferPosition < 0)
                        {
                            if (!_stream.CanSeek)
                                throw new NotSupportedException();

                            _readerBufferPosition = 0;
                            _readerBufferLength = 0;

                            _stream.Position = value;
                        }
                    }
                }

                if (_writerBlockBuffer != null)
                {
                    this.Flush();
                }

                _position = value;
            }
        }

        public override long Length
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.Length;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            if (origin == SeekOrigin.Begin)
            {
                return this.Position = offset;
            }
            else if (origin == SeekOrigin.Current)
            {
                return this.Position += offset;
            }
            else if (origin == SeekOrigin.End)
            {
                return this.Position = this.Length + offset;
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        public override void SetLength(long value)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _stream.SetLength(value);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed)
                throw new ObjectDisposedException(this.GetType().FullName);
            if (offset < 0 || buffer.Length < offset)
                throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || (buffer.Length - offset) < count)
                throw new ArgumentOutOfRangeException("count");

            //count = Math.Min(count, (int)this.Length - (int)this.Position);

            if (_readerBuffer == null)
            {
                _readerBuffer = _bufferManager.TakeBuffer(_bufferSize);
                _readerBufferGcHandle = System.Runtime.InteropServices.GCHandle.Alloc(_readerBuffer);
            }

            if (!_isReadMode)
            {
                this.Flush();

                _isReadMode = true;
            }

            int readSumLength = 0;

            if ((_readerBuffer.Length - _readerBufferPosition) + _readerBuffer.Length < count)
            {
                int length = Math.Min(_readerBufferLength - _readerBufferPosition, count);
                Array.Copy(_readerBuffer, _readerBufferPosition, buffer, offset, length);
                _readerBufferPosition += length;
                offset += length;
                count -= length;
                readSumLength += length;

                int readLength = 0;

                while (count > 0 && (readLength = _stream.Read(buffer, offset, count)) > 0)
                {
                    offset += readLength;
                    count -= readLength;
                    readSumLength += readLength;
                }
            }
            else
            {
                while (0 < count)
                {
                    if (_readerBufferLength == _readerBufferPosition)
                    {
                        int tOffset = 0;
                        int tCount = _readerBuffer.Length;
                        int readLength = 0;
                        _readerBufferLength = 0;

                        while (tCount > 0 && 0 < (readLength = _stream.Read(_readerBuffer, tOffset, tCount)))
                        {
                            tOffset += readLength;
                            tCount -= readLength;
                            _readerBufferLength += readLength;
                        }

                        _readerBufferPosition = 0;
                        if (_readerBufferLength == 0)
                            break;
                    }

                    int length = Math.Min(_readerBufferLength - _readerBufferPosition, count);
                    Array.Copy(_readerBuffer, _readerBufferPosition, buffer, offset, length);
                    _readerBufferPosition += length;
                    offset += length;
                    count -= length;
                    readSumLength += length;
                }
            }

            _position += readSumLength;
            return readSumLength;

            //return _stream.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed)
                throw new ObjectDisposedException(this.GetType().FullName);
            if (offset < 0 || buffer.Length < offset)
                throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || (buffer.Length - offset) < count)
                throw new ArgumentOutOfRangeException("count");

            if (_writerBlockBuffer == null)
            {
                _writerBlockBuffer = _bufferManager.TakeBuffer(_bufferSize);
                _writerBufferGcHandle = System.Runtime.InteropServices.GCHandle.Alloc(_writerBlockBuffer);
            }

            if (_isReadMode)
            {
                _readerBufferLength = 0;
                _readerBufferPosition = 0;

                try
                {
                    _stream.Position = _position;
                }
                catch (Exception)
                {

                }

                _isReadMode = false;
            }

            int writeSumLength = 0;

            if ((_writerBlockBuffer.Length - _writerBufferPosition) + _writerBlockBuffer.Length < count)
            {
                if (_writerBlockBuffer.Length == _writerBufferPosition)
                {
                    _stream.Write(_writerBlockBuffer, 0, _writerBufferPosition);
                    _writerBufferPosition = 0;
                }

                _stream.Write(buffer, offset, count);
                writeSumLength += count;
            }
            else
            {
                while (0 < count)
                {
                    int length = Math.Min(_writerBlockBuffer.Length - _writerBufferPosition, count);
                    Array.Copy(buffer, offset, _writerBlockBuffer, _writerBufferPosition, length);
                    _writerBufferPosition += length;
                    offset += length;
                    count -= length;
                    writeSumLength += length;

                    if (_writerBlockBuffer.Length == _writerBufferPosition)
                    {
                        _stream.Write(_writerBlockBuffer, 0, _writerBufferPosition);
                        _writerBufferPosition = 0;
                    }
                }
            }

            _position += writeSumLength;

            //_stream.Write(buffer, offset, count);
        }

        public override void Flush()
        {
            if (_disposed)
                throw new ObjectDisposedException(this.GetType().FullName);

            if (_writerBlockBuffer != null && _writerBufferPosition != 0)
            {
                _stream.Write(_writerBlockBuffer, 0, _writerBufferPosition);
                _writerBufferPosition = 0;
            }

            _stream.Flush();
        }

        public override void Close()
        {
            if (!_disposed)
                this.Flush();

            base.Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                try
                {
                    if (disposing)
                    {
                        if (_stream != null && !_leaveInnerStreamOpen)
                        {
                            try
                            {
                                _stream.Dispose();
                            }
                            catch (Exception)
                            {

                            }

                            _stream = null;
                        }

                        if (_readerBuffer != null)
                        {
                            try
                            {
                                _bufferManager.ReturnBuffer(_readerBuffer);
                            }
                            catch (Exception)
                            {

                            }

                            _readerBuffer = null;
                        }

                        if (_writerBlockBuffer != null)
                        {
                            try
                            {
                                _bufferManager.ReturnBuffer(_writerBlockBuffer);
                            }
                            catch (Exception)
                            {

                            }

                            _writerBlockBuffer = null;
                        }
                    }

                    if (_readerBufferGcHandle.IsAllocated) _readerBufferGcHandle.Free();
                    if (_writerBufferGcHandle.IsAllocated) _writerBufferGcHandle.Free();

                    _disposed = true;
                }
                finally
                {
                    base.Dispose(disposing);
                }
            }
        }
    }
}

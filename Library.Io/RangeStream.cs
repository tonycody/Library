using System;
using System.IO;

namespace Library.Io
{
    public class RangeStream : Stream
    {
        private Stream _stream;
        private long _position;
        private long _length;
        private bool _leaveInnerStreamOpen;

        private long _orignalLength;
        private bool _disposed;

        public RangeStream(Stream stream, long position, long length, bool leaveInnerStreamOpen)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (position < 0 || stream.Length < position) throw new ArgumentOutOfRangeException("offset");
            if (length < 0 || (stream.Length - position) < length) throw new ArgumentOutOfRangeException("length");

            _stream = stream;
            _position = position;
            _length = length;
            _leaveInnerStreamOpen = leaveInnerStreamOpen;
            _orignalLength = length;

            if (_stream.Position != _position)
                _stream.Seek(_position, SeekOrigin.Begin);
        }

        public RangeStream(Stream stream, long offset, long length)
            : this(stream, offset, length, false)
        {

        }

        public RangeStream(Stream stream, bool leaveInnerStreamOpen)
            : this(stream, stream.Position, stream.Length - stream.Position, leaveInnerStreamOpen)
        {

        }

        public RangeStream(Stream stream)
            : this(stream, false)
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

                return false;
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
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.Position - _position;
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                if (value < 0 || this.Length < value) throw new ArgumentOutOfRangeException("Position");
                if (!_stream.CanSeek) throw new NotSupportedException();

                _stream.Position = value + _position;
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
            if (value < 0 || _orignalLength < value) throw new ArgumentOutOfRangeException("value");

            _length = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (offset < 0 || buffer.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || (buffer.Length - offset) < count) throw new ArgumentOutOfRangeException("count");
            if (count == 0) return 0;

            count = (int)Math.Min(count, this.Length - this.Position);

            if (count == 0)
                return 0;

            int readSumLength = 0;
            int readLength = 0;

            while (count > 0 && (readLength = _stream.Read(buffer, offset, count)) > 0)
            {
                offset += readLength;
                count -= readLength;
                readSumLength += readLength;
            }

            return readSumLength;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
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
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }
    }
}

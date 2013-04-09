using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Library.Io
{
    public class JoinStream : Stream
    {
        private List<Stream> _streams = new List<Stream>();
        private long _position;
        private long _length;
        private bool _disposed = false;

        public JoinStream(params Stream[] streams)
        {
            foreach (var item in streams)
            {
                if (item != null && item.Length != 0 && item.CanRead && item.CanSeek)
                    _streams.Add(item);
            }

            _position = 0;
            _length = _streams.Sum(n => n.Length);
        }

        public JoinStream(IEnumerable<Stream> streams)
        {
            foreach (var item in streams)
            {
                if (item != null && item.Length != 0 && item.CanRead && item.CanSeek)
                    _streams.Add(item);
            }

            _position = 0;
            _length = _streams.Sum(n => n.Length);
        }

        public override bool CanRead
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
                if (value < 0 || this.Length < value) throw new ArgumentOutOfRangeException("Position");

                _position = value;
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
            if (value < 0 || _length < value) throw new ArgumentOutOfRangeException("value");

            _length = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (offset < 0 || buffer.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || (buffer.Length - offset) < count) throw new ArgumentOutOfRangeException("count");
            if (count == 0) return 0;

            count = (int)Math.Min(count, this.Length - this.Position);

            int index = 0;
            int position = 0;

            for (long p = 0; index < _streams.Count; index++)
            {
                p += _streams[index].Length;

                if (this.Position < p)
                {
                    position = (int)(_streams[index].Length - (p - this.Position));
                    break;
                }
            }

            int readSumLength = 0;

            for (; 0 < count && index < _streams.Count; index++)
            {
                int length = (int)Math.Min(_streams[index].Length - (long)position, count);

                _streams[index].Seek(position, SeekOrigin.Begin);
                position = 0;

                int readLength = 0;

                while (0 < length && 0 < (readLength = _streams[index].Read(buffer, offset, length)))
                {
                    offset += readLength;
                    length -= readLength;
                    count -= readLength;
                    readSumLength += readLength;
                }
            }

            this.Position += readSumLength;
            return readSumLength;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
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

                if (disposing)
                {
                    if (_streams != null)
                    {
                        foreach (var item in _streams)
                        {
                            try
                            {
                                item.Dispose();
                            }
                            catch (Exception)
                            {

                            }
                        }

                        _streams = null;
                    }
                }

                _disposed = true;
            }
            finally
            {
                base.Dispose(disposing);
            }
        }
    }
}

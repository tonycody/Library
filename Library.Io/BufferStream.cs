using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Library.Io
{
    public class BufferStream : Stream
    {
        private BufferManager _bufferManager;
        private List<byte[]> _buffers = new List<byte[]>();
        private long _position;
        private long _length;
        private int _bufferSize = 4096;
        private bool _disposed;

        public BufferStream(BufferManager bufferManager)
        {
            _bufferManager = bufferManager;
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

            {
                long sum = 0;

                for (int i = 0; i < _buffers.Count; i++)
                {
                    sum += _buffers[i].Length;
                }

                while (sum < value)
                {
                    var buffer = _bufferManager.TakeBuffer(_bufferSize);
                    if (_bufferSize < 1024 * 32) _bufferSize *= 2;

                    _buffers.Add(buffer);
                    sum += buffer.Length;
                }
            }

            _length = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (offset < 0 || buffer.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || (buffer.Length - offset) < count) throw new ArgumentOutOfRangeException("count");

            count = (int)Math.Min(count, this.Length - this.Position);
            if (count == 0) return 0;

            int index = 0;
            int position = 0;

            for (long p = 0; index < _buffers.Count; index++)
            {
                p += _buffers[index].Length;

                if (this.Position < p)
                {
                    position = _buffers[index].Length - (int)(p - this.Position);
                    break;
                }
            }

            int readSumLength = 0;

            for (; count > 0 && index < _buffers.Count; index++)
            {
                int length = Math.Min(_buffers[index].Length - position, count);

                Native.Copy(_buffers[index], position, buffer, offset, length);
                position = 0;

                offset += length;
                count -= length;
                readSumLength += length;
            }

            this.Position += readSumLength;
            return readSumLength;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (offset < 0 || buffer.Length < offset) throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || (buffer.Length - offset) < count) throw new ArgumentOutOfRangeException("count");
            if (count == 0) return;

            if (this.Length < this.Position + count)
            {
                this.SetLength(this.Position + count);
            }

            int index = 0;
            int position = 0;

            for (long p = 0; index < _buffers.Count; index++)
            {
                p += _buffers[index].Length;

                if (this.Position < p)
                {
                    position = _buffers[index].Length - (int)(p - this.Position);
                    break;
                }
            }

            int writeSumLength = 0;

            for (; count > 0 && index < _buffers.Count; index++)
            {
                int length = Math.Min(_buffers[index].Length - position, count);

                Native.Copy(buffer, offset, _buffers[index], position, length);
                position = 0;

                offset += length;
                count -= length;
                writeSumLength += length;
            }

            this.Position += writeSumLength;
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
                    if (_buffers != null)
                    {
                        try
                        {
                            for (int i = 0; i < _buffers.Count; i++)
                            {
                                _bufferManager.ReturnBuffer(_buffers[i]);
                            }
                        }
                        catch (Exception)
                        {

                        }

                        _buffers = null;
                    }
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        public ArraySegment<byte> ToArray()
        {
            byte[] buffer = _bufferManager.TakeBuffer((int)this.Length);
            long position = this.Position;

            this.Seek(0, SeekOrigin.Begin);
            this.Read(buffer, 0, (int)this.Length);
            this.Position = position;

            return new ArraySegment<byte>(buffer, 0, (int)this.Length);
        }
    }
}

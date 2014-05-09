using System;
using System.IO;
using System.Linq;
using System.Threading;
using Library.Collections;

namespace Library.Io
{
    public class QueueStream : Stream
    {
        private Stream _stream;

        private const int BlockSize = 1024 * 256;

        private bool _disposed;

        public QueueStream(Stream stream, StreamMode mode, int bufferSize, BufferManager bufferManager)
        {
            if (mode == StreamMode.Read)
            {
                _stream = new ReadStream(stream, bufferSize, bufferManager);
            }
            else if (mode == StreamMode.Write)
            {
                _stream = new WriteStream(new CacheStream(stream, QueueStream.BlockSize, bufferManager), bufferSize, bufferManager);
            }
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
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.Position;
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _stream.Position = value;
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

            return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _stream.SetLength(value);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            return _stream.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _stream.Write(buffer, offset, count);
        }

        public override void Flush()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _stream.Flush();
        }

        public override void Close()
        {
            if (_disposed) return;

            this.Flush();
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
                    if (_stream != null)
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

        private class ReadStream : Stream
        {
            private Stream _stream;
            private int _bufferSize;
            private BufferManager _bufferManager;

            private Thread _watchThread;

            private WaitQueue<ArraySegment<byte>> _queue;
            private ArraySegment<byte>? _current = null;

            private long _position = 0;

            private bool _disposed;

            public ReadStream(Stream stream, int bufferSize, BufferManager bufferManager)
            {
                _stream = stream;
                _bufferSize = bufferSize;
                _bufferManager = bufferManager;

                _queue = new WaitQueue<ArraySegment<byte>>(Math.Max(8, _bufferSize / QueueStream.BlockSize));

                _watchThread = new Thread(this.WatchThread);
                _watchThread.Priority = ThreadPriority.BelowNormal;
                _watchThread.Name = "QueueStream_WatchThread";
                _watchThread.Start();
            }

            private void WatchThread()
            {
                for (; ; )
                {
                    byte[] buffer = null;

                    try
                    {
                        buffer = _bufferManager.TakeBuffer(QueueStream.BlockSize);
                        int length = _stream.Read(buffer, 0, buffer.Length);

                        if (length > 0)
                        {
                            _queue.Enqueue(new ArraySegment<byte>(buffer, 0, length));
                        }
                        else
                        {
                            _bufferManager.ReturnBuffer(buffer);

                            _queue.Enqueue(new ArraySegment<byte>());

                            return;
                        }
                    }
                    catch (Exception)
                    {
                        if (buffer != null)
                        {
                            _bufferManager.ReturnBuffer(buffer);
                        }

                        return;
                    }
                }
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

                    return _stream.Length;
                }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _stream.SetLength(value);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                if (offset < 0 || buffer.Length < offset) throw new ArgumentOutOfRangeException("offset");
                if (count < 0 || (buffer.Length - offset) < count) throw new ArgumentOutOfRangeException("count");

                try
                {
                    int readSumLength = 0;

                    for (; ; )
                    {
                        if (_current == null) _current = _queue.Dequeue();
                        if (_current.Value.Array == null) return 0;

                        var subCount = Math.Min(count, _current.Value.Count);
                        readSumLength += subCount;

                        Unsafe.Copy(_current.Value.Array, _current.Value.Offset, buffer, offset, subCount);

                        offset += subCount;
                        count -= subCount;

                        if (subCount < _current.Value.Count)
                        {
                            _current = new ArraySegment<byte>(_current.Value.Array, _current.Value.Offset + subCount, _current.Value.Count - subCount);
                        }
                        else
                        {
                            _bufferManager.ReturnBuffer(_current.Value.Array);
                            _current = null;
                        }

                        if (count == 0)
                        {
                            _position += readSumLength;

                            return readSumLength;
                        }
                    }
                }
                catch (Exception e)
                {
                    throw new StopIoException("QueueStream Read", e);
                }
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

                this.Flush();
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
                        _queue.Dispose();

                        if (_watchThread != null)
                        {
                            try
                            {
                                _watchThread.Join();
                            }
                            catch (Exception)
                            {

                            }

                            _watchThread = null;
                        }

                        if (_stream != null)
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

        public class WriteStream : Stream
        {
            private Stream _stream;
            private int _bufferSize;
            private BufferManager _bufferManager;

            private Thread _watchThread;

            private WaitQueue<ArraySegment<byte>> _queue;

            private long _position = 0;

            private bool _disposed;

            public WriteStream(Stream stream, int bufferSize, BufferManager bufferManager)
            {
                _stream = stream;
                _bufferSize = bufferSize;
                _bufferManager = bufferManager;

                _queue = new WaitQueue<ArraySegment<byte>>(Math.Max(8, _bufferSize / QueueStream.BlockSize));

                _watchThread = new Thread(this.WatchThread);
                _watchThread.Priority = ThreadPriority.BelowNormal;
                _watchThread.Name = "QueueStream_WatchThread";
                _watchThread.Start();
            }

            private void WatchThread()
            {
                for (; ; )
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>();

                    try
                    {
                        buffer = _queue.Dequeue();
                        if (buffer.Array == null) return;

                        _stream.Write(buffer.Array, buffer.Offset, buffer.Count);
                    }
                    catch (Exception)
                    {
                        return;
                    }
                    finally
                    {
                        if (buffer.Array != null)
                        {
                            _bufferManager.ReturnBuffer(buffer.Array);
                        }
                    }
                }
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

                    return _stream.Length;
                }
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _stream.SetLength(value);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                try
                {
                    var tempBuffer = _bufferManager.TakeBuffer(QueueStream.BlockSize);
                    Unsafe.Copy(buffer, offset, tempBuffer, 0, count);

                    _queue.Enqueue(new ArraySegment<byte>(tempBuffer, 0, count));

                    _position += count;
                }
                catch (Exception e)
                {
                    throw new StopIoException("QueueStream Read", e);
                }
            }

            public override void Flush()
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _queue.Enqueue(new ArraySegment<byte>());
                _watchThread.Join();
            }

            public override void Close()
            {
                if (_disposed) return;

                this.Flush();
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
                        _queue.Dispose();

                        if (_watchThread != null)
                        {
                            try
                            {
                                _watchThread.Join();
                            }
                            catch (Exception)
                            {

                            }

                            _watchThread = null;
                        }

                        if (_stream != null)
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
}

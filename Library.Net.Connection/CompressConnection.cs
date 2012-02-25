using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Library.Io;
using System.Threading;
using System.IO.Compression;
using System.Diagnostics;

namespace Library.Net.Connection
{
    public class CompressConnection : ConnectionBase, IThisLock
    {
        private enum CompressAlgorithm
        {
            None = 0,
            GZip = 1,
        }

        private ConnectionBase _connection;
        private int _maxReceiveCount;
        private BufferManager _bufferManager;

        private bool _disposed = false;

        private object _sendLock = new object();
        private object _receiveLock = new object();
        private object _thisLock = new object();

        public CompressConnection(ConnectionBase connection, int maxReceiveCount, BufferManager bufferManager)
        {
            _connection = connection;
            _maxReceiveCount = maxReceiveCount;
            _bufferManager = bufferManager;
        }

        public override long ReceivedByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connection.ReceivedByteCount;
            }
        }

        public override long SentByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _connection.SentByteCount;
            }
        }

        public ConnectionBase Connection
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _connection;
                }
            }
        }

        public override void Connect(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {

            }
        }

        public override void Close(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_connection != null)
                {
                    try
                    {
                        _connection.Close(timeout);
                        _connection.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _connection = null;
                }
            }
        }

        public override System.IO.Stream Receive(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(_receiveLock))
            {
                Stream stream = null;
                Stream dataStream = null;

                try
                {
                    stream = _connection.Receive(timeout);

                    byte version = (byte)stream.ReadByte();

                    dataStream = new RangeStream(stream, stream.Position, stream.Length - stream.Position);
                    dataStream.Seek(0, SeekOrigin.Begin);

                    if (version == (byte)CompressAlgorithm.None)
                    {
                        return dataStream;
                    }
                    else if (version == (byte)CompressAlgorithm.GZip)
                    {
                        BufferStream gzipBufferStream = null;

                        try
                        {
                            gzipBufferStream = new BufferStream(_bufferManager);
                            byte[] decompressBuffer = null;

                            try
                            {
                                decompressBuffer = _bufferManager.TakeBuffer(1024 * 256);

                                using (GZipStream gzipStream = new GZipStream(stream, CompressionMode.Decompress, true))
                                {
                                    int i = -1;

                                    while ((i = gzipStream.Read(decompressBuffer, 0, decompressBuffer.Length)) > 0)
                                    {
                                        gzipBufferStream.Write(decompressBuffer, 0, i);

                                        if (gzipBufferStream.Length > _maxReceiveCount) throw new ConnectionException();
                                    }
                                }
                            }
                            finally
                            {
                                _bufferManager.ReturnBuffer(decompressBuffer);
                            }
                        }
                        catch (Exception ex)
                        {
                            gzipBufferStream.Dispose();

                            throw ex;
                        }

                        Debug.WriteLine("Receive gzip : {0} {1}", NetworkConverter.ToSizeString(gzipBufferStream.Length), NetworkConverter.ToSizeString(stream.Length - gzipBufferStream.Length));

                        gzipBufferStream.Seek(0, SeekOrigin.Begin);
                        stream.Dispose();
                        dataStream.Dispose();

                        return gzipBufferStream;
                    }
                    else
                    {
                        throw new ArgumentException("ArgumentException");
                    }
                }
                catch (ConnectionException ex)
                {
                    if (stream != null) stream.Dispose();
                    if (dataStream != null) dataStream.Dispose();

                    throw ex;
                }
                catch (Exception e)
                {
                    if (stream != null) stream.Dispose();
                    if (dataStream != null) dataStream.Dispose();

                    throw new ConnectionException(e.Message, e);
                }
            }
        }

        public override void Send(System.IO.Stream stream, TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");

            using (DeadlockMonitor.Lock(_sendLock))
            {
                try
                {
                    BufferStream gzipBufferStream = null;

                    try
                    {
                        gzipBufferStream = new BufferStream(_bufferManager);
                        byte[] compressBuffer = null;

                        try
                        {
                            compressBuffer = _bufferManager.TakeBuffer(1024 * 256);

                            using (GZipStream gzipStream = new GZipStream(gzipBufferStream, CompressionMode.Compress, true))
                            {
                                int i = -1;

                                while ((i = stream.Read(compressBuffer, 0, compressBuffer.Length)) > 0)
                                {
                                    gzipStream.Write(compressBuffer, 0, i);
                                }
                            }
                        }
                        finally
                        {
                            _bufferManager.ReturnBuffer(compressBuffer);
                        }
                    }
                    catch (Exception ex)
                    {
                        gzipBufferStream.Dispose();

                        throw ex;
                    }

                    gzipBufferStream.Seek(0, SeekOrigin.Begin);

                    BufferStream headerStream = new BufferStream(_bufferManager);
                    Stream dataStream = null;

                    if (stream.Length < gzipBufferStream.Length)
                    {
                        headerStream.WriteByte((byte)CompressAlgorithm.None);
                        dataStream = new AddStream(headerStream, new RangeStream(stream, true));

                        gzipBufferStream.Dispose();
                    }
                    else
                    {
                        headerStream.WriteByte((byte)CompressAlgorithm.GZip);
                        dataStream = new AddStream(headerStream, gzipBufferStream);

                        Debug.WriteLine("Send gzip : {0} {1}", NetworkConverter.ToSizeString(stream.Length), NetworkConverter.ToSizeString(gzipBufferStream.Length - stream.Length));
                    }

                    using (dataStream)
                    {
                        _connection.Send(dataStream, timeout);
                    }
                }
                catch (ConnectionException ex)
                {
                    throw ex;
                }
                catch (Exception e)
                {
                    throw new ConnectionException(e.Message, e);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_connection != null)
                    {
                        try
                        {
                            _connection.Dispose();
                        }
                        catch (Exception)
                        {

                        }

                        _connection = null;
                    }
                }

                _disposed = true;
            }
        }

        #region IThisLock メンバ

        public object ThisLock
        {
            get
            {
                return _thisLock;
            }
        }

        #endregion
    }
}

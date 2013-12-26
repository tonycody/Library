using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Library.Io;

namespace Library.Net.Connections
{
    public class CompressConnection : ConnectionBase, IThisLock
    {
        [Flags]
        private enum CompressAlgorithm : uint
        {
            None = 0,
            Deflate = 0x01,
        }

        private ConnectionBase _connection;
        private int _maxReceiveCount;
        private BufferManager _bufferManager;

        private CompressAlgorithm _myCompressAlgorithm;
        private CompressAlgorithm _otherCompressAlgorithm;

        private readonly object _sendLock = new object();
        private readonly object _receiveLock = new object();
        private readonly object _thisLock = new object();

        private volatile bool _connect;
        private volatile bool _disposed;

        public CompressConnection(ConnectionBase connection, int maxReceiveCount, BufferManager bufferManager)
        {
            _connection = connection;
            _maxReceiveCount = maxReceiveCount;
            _bufferManager = bufferManager;

            _myCompressAlgorithm = CompressAlgorithm.Deflate;
        }

        public override IEnumerable<ConnectionBase> GetLayers()
        {
            var list = new List<ConnectionBase>(_connection.GetLayers());
            list.Add(this);

            return list;
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

                lock (this.ThisLock)
                {
                    return _connection;
                }
            }
        }

        public override void Connect(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                try
                {
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();

                    using (BufferStream stream = new BufferStream(_bufferManager))
                    {
                        byte[] buffer = NetworkConverter.GetBytes((uint)_myCompressAlgorithm);
                        stream.Write(buffer, 0, buffer.Length);

                        _connection.Send(stream, CheckTimeout(stopwatch.Elapsed, timeout));
                    }

                    using (Stream stream = _connection.Receive(CheckTimeout(stopwatch.Elapsed, timeout)))
                    {
                        byte[] buffer = new byte[4];
                        stream.Read(buffer, 0, buffer.Length);

                        _otherCompressAlgorithm = (CompressAlgorithm)NetworkConverter.ToUInt32(buffer);
                    }
                }
                catch (ConnectionException ex)
                {
                    throw ex;
                }
                catch (Exception ex)
                {
                    throw new ConnectionException(ex.Message, ex);
                }

                _connect = true;
            }
        }

        public override void Close(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new ConnectionException();

            lock (this.ThisLock)
            {
                if (_connection != null)
                {
                    try
                    {
                        _connection.Close(timeout);
                    }
                    catch (Exception)
                    {

                    }

                    _connection = null;
                }

                _connect = false;
            }
        }

        public override System.IO.Stream Receive(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new ConnectionException();

            lock (_receiveLock)
            {
                Stream stream = null;
                Stream dataStream = null;

                try
                {
                    stream = _connection.Receive(timeout);

                    byte version = (byte)stream.ReadByte();

                    dataStream = new RangeStream(stream, stream.Position, stream.Length - stream.Position);
                    dataStream.Seek(0, SeekOrigin.Begin);

                    if (version == (byte)0)
                    {
                        return dataStream;
                    }
                    else if (version == (byte)1)
                    {
                        BufferStream deflateBufferStream = null;

                        try
                        {
                            deflateBufferStream = new BufferStream(_bufferManager);
                            byte[] decompressBuffer = null;

                            try
                            {
                                decompressBuffer = _bufferManager.TakeBuffer(1024 * 32);

                                using (DeflateStream deflateStream = new DeflateStream(dataStream, CompressionMode.Decompress, true))
                                {
                                    int i = -1;

                                    while ((i = deflateStream.Read(decompressBuffer, 0, decompressBuffer.Length)) > 0)
                                    {
                                        deflateBufferStream.Write(decompressBuffer, 0, i);

                                        if (deflateBufferStream.Length > _maxReceiveCount) throw new ConnectionException();
                                    }
                                }
                            }
                            finally
                            {
                                _bufferManager.ReturnBuffer(decompressBuffer);
                            }
                        }
                        catch (Exception e)
                        {
                            deflateBufferStream.Dispose();

                            throw e;
                        }

#if DEBUG
                        Debug.WriteLine("Receive : {0}→{1} {2}",
                            NetworkConverter.ToSizeString(stream.Length),
                            NetworkConverter.ToSizeString(deflateBufferStream.Length),
                            NetworkConverter.ToSizeString(stream.Length - deflateBufferStream.Length));
#endif
                        deflateBufferStream.Seek(0, SeekOrigin.Begin);
                        dataStream.Dispose();

                        return deflateBufferStream;
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
            if (!_connect) throw new ConnectionException();
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");

            lock (_sendLock)
            {
                try
                {
                    List<KeyValuePair<int, Stream>> list = new List<KeyValuePair<int, Stream>>();

                    if (_otherCompressAlgorithm.HasFlag(CompressAlgorithm.Deflate))
                    {
                        try
                        {
                            BufferStream deflateBufferStream = new BufferStream(_bufferManager);
                            byte[] compressBuffer = null;

                            try
                            {
                                compressBuffer = _bufferManager.TakeBuffer(1024 * 32);

                                using (DeflateStream deflateStream = new DeflateStream(deflateBufferStream, CompressionMode.Compress, true))
                                {
                                    int i = -1;

                                    while ((i = stream.Read(compressBuffer, 0, compressBuffer.Length)) > 0)
                                    {
                                        deflateStream.Write(compressBuffer, 0, i);
                                    }
                                }
                            }
                            finally
                            {
                                _bufferManager.ReturnBuffer(compressBuffer);
                            }

                            deflateBufferStream.Seek(0, SeekOrigin.Begin);
                            list.Add(new KeyValuePair<int, Stream>(1, deflateBufferStream));
                        }
                        catch (Exception)
                        {
                            throw;
                        }
                    }

                    list.Add(new KeyValuePair<int, Stream>(0, new WrapperStream(stream, true)));

                    list.Sort((x, y) =>
                    {
                        return x.Value.Length.CompareTo(y.Value.Length);
                    });

#if DEBUG
                    if (list[0].Value.Length != stream.Length)
                    {
                        Debug.WriteLine("Send : {0}→{1} {2}",
                            NetworkConverter.ToSizeString(stream.Length),
                            NetworkConverter.ToSizeString(list[0].Value.Length),
                            NetworkConverter.ToSizeString(list[0].Value.Length - stream.Length));
                    }
#endif

                    for (int i = 1; i < list.Count; i++)
                    {
                        list[i].Value.Dispose();
                    }

                    BufferStream headerStream = new BufferStream(_bufferManager);
                    headerStream.WriteByte((byte)list[0].Key);

                    using (var dataStream = new JoinStream(headerStream, list[0].Value))
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
            if (_disposed) return;
            _disposed = true;

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

                _connect = false;
            }
        }

        #region IThisLock

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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Library.Io;

namespace Library.Net.Connections
{
    public class CompressConnection : Connection, IThisLock
    {
        [Flags]
        private enum CompressAlgorithm : uint
        {
            None = 0,
            Deflate = 0x01,
        }

        private Connection _connection;
        private int _maxReceiveCount;
        private BufferManager _bufferManager;

        private CompressAlgorithm _myCompressAlgorithm;
        private CompressAlgorithm _otherCompressAlgorithm;

        private readonly object _sendLock = new object();
        private readonly object _receiveLock = new object();
        private readonly object _thisLock = new object();

        private volatile bool _connect;

        private volatile bool _disposed;

        public CompressConnection(Connection connection, int maxReceiveCount, BufferManager bufferManager)
        {
            _connection = connection;
            _maxReceiveCount = maxReceiveCount;
            _bufferManager = bufferManager;

            _myCompressAlgorithm = CompressAlgorithm.Deflate;
        }

        public override IEnumerable<Connection> GetLayers()
        {
            var list = new List<Connection>(_connection.GetLayers());
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

        public Connection Connection
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

        public override void Connect(TimeSpan timeout, Information options)
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
                        stream.Flush();
                        stream.Seek(0, SeekOrigin.Begin);

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

        public override void Close(TimeSpan timeout, Information options)
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

        public override System.IO.Stream Receive(TimeSpan timeout, Information options)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new ConnectionException();

            lock (_receiveLock)
            {
                Stream stream = null;

                try
                {
                    stream = _connection.Receive(timeout, options);

                    byte version = (byte)stream.ReadByte();

                    Stream dataStream = null;

                    try
                    {
                        dataStream = new RangeStream(stream, stream.Position, stream.Length - stream.Position);

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

                                using (DeflateStream deflateStream = new DeflateStream(dataStream, CompressionMode.Decompress, true))
                                {
                                    byte[] decompressBuffer = null;

                                    try
                                    {
                                        decompressBuffer = _bufferManager.TakeBuffer(1024 * 4);

                                        int i = -1;

                                        while ((i = deflateStream.Read(decompressBuffer, 0, decompressBuffer.Length)) > 0)
                                        {
                                            deflateBufferStream.Write(decompressBuffer, 0, i);

                                            if (deflateBufferStream.Length > _maxReceiveCount) throw new ConnectionException();
                                        }
                                    }
                                    finally
                                    {
                                        if (decompressBuffer != null)
                                        {
                                            _bufferManager.ReturnBuffer(decompressBuffer);
                                        }
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                if (deflateBufferStream != null)
                                {
                                    deflateBufferStream.Dispose();
                                }

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
                    catch (ConnectionException e)
                    {
                        if (dataStream != null) dataStream.Dispose();

                        throw e;
                    }
                    catch (Exception e)
                    {
                        if (dataStream != null) dataStream.Dispose();

                        throw new ConnectionException(e.Message, e);
                    }
                }
                catch (ConnectionException e)
                {
                    if (stream != null) stream.Dispose();

                    throw e;
                }
                catch (Exception e)
                {
                    if (stream != null) stream.Dispose();

                    throw new ConnectionException(e.Message, e);
                }
            }
        }

        public override void Send(System.IO.Stream stream, TimeSpan timeout, Information options)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new ConnectionException();
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");

            bool isCompress = true;

            if (options != null)
            {
                if (options.Contains("IsCompress")) isCompress = (bool)options["IsCompress"];
            }

            lock (_sendLock)
            {
                using (RangeStream targetStream = new RangeStream(stream, stream.Position, stream.Length - stream.Position, true))
                {
                    try
                    {
                        List<KeyValuePair<byte, Stream>> list = new List<KeyValuePair<byte, Stream>>();

                        if (isCompress)
                        {
                            if (_otherCompressAlgorithm.HasFlag(CompressAlgorithm.Deflate))
                            {
                                BufferStream deflateBufferStream = null;

                                try
                                {
                                    deflateBufferStream = new BufferStream(_bufferManager);

                                    using (DeflateStream deflateStream = new DeflateStream(deflateBufferStream, CompressionMode.Compress, true))
                                    {
                                        byte[] compressBuffer = null;

                                        try
                                        {
                                            compressBuffer = _bufferManager.TakeBuffer(1024 * 4);

                                            int i = -1;

                                            while ((i = targetStream.Read(compressBuffer, 0, compressBuffer.Length)) > 0)
                                            {
                                                deflateStream.Write(compressBuffer, 0, i);
                                            }
                                        }
                                        finally
                                        {
                                            if (compressBuffer != null)
                                            {
                                                _bufferManager.ReturnBuffer(compressBuffer);
                                            }
                                        }
                                    }

                                    deflateBufferStream.Seek(0, SeekOrigin.Begin);

                                    list.Add(new KeyValuePair<byte, Stream>((byte)1, deflateBufferStream));
                                }
                                catch (Exception e)
                                {
                                    if (deflateBufferStream != null)
                                    {
                                        deflateBufferStream.Dispose();
                                    }

                                    throw e;
                                }
                            }
                        }

                        list.Add(new KeyValuePair<byte, Stream>((byte)0, new WrapperStream(targetStream, true)));

                        list.Sort((x, y) =>
                        {
                            int c = x.Value.Length.CompareTo(y.Value.Length);
                            if (c != 0) return c;

                            return x.Key.CompareTo(y.Key);
                        });

#if DEBUG
                        if (list[0].Value.Length != targetStream.Length)
                        {
                            Debug.WriteLine("Send : {0}→{1} {2}",
                                NetworkConverter.ToSizeString(targetStream.Length),
                                NetworkConverter.ToSizeString(list[0].Value.Length),
                                NetworkConverter.ToSizeString(list[0].Value.Length - targetStream.Length));
                        }
#endif

                        for (int i = 1; i < list.Count; i++)
                        {
                            list[i].Value.Dispose();
                        }

                        BufferStream headerStream = new BufferStream(_bufferManager);
                        headerStream.WriteByte((byte)list[0].Key);

                        using (var dataStream = new UniteStream(headerStream, list[0].Value))
                        {
                            _connection.Send(dataStream, timeout, options);
                        }
                    }
                    catch (ConnectionException e)
                    {
                        throw e;
                    }
                    catch (Exception e)
                    {
                        throw new ConnectionException(e.Message, e);
                    }
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

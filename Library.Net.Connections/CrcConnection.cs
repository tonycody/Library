using System;
using System.Collections.Generic;
using System.IO;
using Library.Io;
using Library.Security;

namespace Library.Net.Connections
{
    public class CrcConnection : Connection, IThisLock
    {
        private Connection _connection;
        private BufferManager _bufferManager;

        private readonly object _sendLock = new object();
        private readonly object _receiveLock = new object();
        private readonly object _thisLock = new object();

        private volatile bool _connect;

        private volatile bool _disposed;

        public CrcConnection(Connection connection, BufferManager bufferManager)
        {
            _connection = connection;
            _bufferManager = bufferManager;

            _connect = true;
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
            throw new NotSupportedException();
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
                RangeStream dataStream = null;

                try
                {
                    stream = _connection.Receive(timeout, options);

                    dataStream = new RangeStream(stream, 0, stream.Length - 4);
                    byte[] verifyCrc = Crc32_Castagnoli.ComputeHash(dataStream);
                    byte[] orignalCrc = new byte[4];

                    using (RangeStream crcStream = new RangeStream(stream, stream.Length - 4, 4, true))
                    {
                        crcStream.Read(orignalCrc, 0, orignalCrc.Length);
                    }

                    if (!Unsafe.Equals(verifyCrc, orignalCrc)) throw new ArgumentException("Crc Error");

                    dataStream.Seek(0, SeekOrigin.Begin);
                    return dataStream;
                }
                catch (ConnectionException e)
                {
                    if (stream != null) stream.Dispose();
                    if (dataStream != null) dataStream.Dispose();

                    throw e;
                }
                catch (Exception e)
                {
                    if (stream != null) stream.Dispose();
                    if (dataStream != null) dataStream.Dispose();

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

            lock (_sendLock)
            {
                using (RangeStream targetStream = new RangeStream(stream, stream.Position, stream.Length - stream.Position, true))
                {
                    try
                    {
                        using (MemoryStream crcStream = new MemoryStream(Crc32_Castagnoli.ComputeHash(targetStream)))
                        using (Stream dataStream = new UniteStream(new WrapperStream(targetStream, true), crcStream))
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

    [Serializable]
    public class CrcErrorException : ConnectionException
    {
        public CrcErrorException() : base() { }
        public CrcErrorException(string message) : base(message) { }
        public CrcErrorException(string message, Exception innerException) : base(message, innerException) { }
    }
}

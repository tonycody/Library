using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Library.Io;

namespace Library.Net.Connection
{
    public class TcpConnection : ConnectionBase, IThisLock
    {
        private Socket _socket;
        private IPEndPoint _remoteEndPoint;
        private int _maxReceiveCount;
        private BufferManager _bufferManager;

        private long _receivedByteCount;
        private long _sentByteCount;

        private DateTime _sendUpdateTime;
        private readonly TimeSpan _sendTimeSpan = new TimeSpan(0, 6, 0);
        private readonly TimeSpan _receiveTimeSpan = new TimeSpan(0, 6, 0);
        private readonly TimeSpan _aliveTimeSpan = new TimeSpan(0, 3, 0);

        private object _sendLock = new object();
        private object _receiveLock = new object();
        private object _thisLock = new object();

        private volatile bool _connect = false;
        private bool _disposed = false;

        private TcpConnection(int maxReceiveCount, BufferManager bufferManager)
        {
            _maxReceiveCount = maxReceiveCount;
            _bufferManager = bufferManager;
        }

        public TcpConnection(Socket socket, int maxReceiveCount, BufferManager bufferManager)
            : this(maxReceiveCount, bufferManager)
        {
            _socket = socket;
            //_socket.ReceiveBufferSize = 1024 * 1024;
            //_socket.SendBufferSize = 1024 * 1024;

            ThreadPool.QueueUserWorkItem(new WaitCallback(this.AliveTimer));
            _sendUpdateTime = DateTime.UtcNow;

            _connect = true;
        }

        public TcpConnection(IPEndPoint remoteEndPoint, int maxReceiveCount, BufferManager bufferManager)
            : this(maxReceiveCount, bufferManager)
        {
            _remoteEndPoint = remoteEndPoint;
            _socket = new Socket(remoteEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            //_socket.ReceiveBufferSize = 1024 * 1024;
            //_socket.SendBufferSize = 1024 * 1024;
        }

        public override long ReceivedByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _receivedByteCount;
            }
        }

        public override long SentByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _sentByteCount;
            }
        }

        public override void Connect(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                try
                {
                    var asyncResult = _socket.BeginConnect(_remoteEndPoint, null, null);

                    if (!asyncResult.IsCompleted && !asyncResult.CompletedSynchronously)
                    {
                        if (!asyncResult.AsyncWaitHandle.WaitOne(timeout, false))
                        {
                            throw new ConnectionException();
                        }
                    }

                    _socket.EndConnect(asyncResult);

                    ThreadPool.QueueUserWorkItem(new WaitCallback(this.AliveTimer));
                    _sendUpdateTime = DateTime.UtcNow;
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

        private void AliveTimer(object state)
        {
            Thread.CurrentThread.Name = "AliveTimer";

            try
            {
                for (; ; )
                {
                    if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                    while ((DateTime.UtcNow - _sendUpdateTime) < _aliveTimeSpan)
                    {
                        Thread.Sleep(1000 * 1);
                    }

                    this.Alive();
                }
            }
            catch (Exception)
            {

            }
        }

        private void Alive()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_sendLock)
            {
                byte[] buffer = new byte[4];

                _socket.SendTimeout = (int)_sendTimeSpan.TotalMilliseconds;
                _socket.Send(buffer, 0, buffer.Length, SocketFlags.None);
                _sendUpdateTime = DateTime.UtcNow;
            }
        }

        public override void Close(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (this.ThisLock)
            {
                if (_socket != null)
                {
                    try
                    {
                        _socket.Close(timeout.Seconds);
                    }
                    catch (Exception)
                    {

                    }

                    _socket = null;
                }
            }
        }

        public override Stream Receive(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new ConnectionException();

            lock (_receiveLock)
            {
                Stopwatch stopwatch = null;

                try
                {
                    stopwatch = new Stopwatch();
                    stopwatch.Start();

                Restart: ;

                    byte[] lengthbuffer = new byte[4];
                    _socket.ReceiveTimeout = Math.Min((int)_receiveTimeSpan.TotalMilliseconds, (int)Math.Min((long)int.MaxValue, (long)TcpConnection.CheckTimeout(stopwatch.Elapsed, timeout).TotalMilliseconds));
                    if (_socket.Receive(lengthbuffer) != lengthbuffer.Length) throw new ConnectionException();
                    _receivedByteCount += 4;
                    int length = NetworkConverter.ToInt32(lengthbuffer);

                    if (length == 0)
                    {
                        Thread.Sleep(1000);
                        goto Restart;
                    }
                    else if (length > _maxReceiveCount)
                    {
                        throw new ConnectionException();
                    }

                    BufferStream bufferStream = null;

                    try
                    {
                        bufferStream = new BufferStream(_bufferManager);
                        byte[] receiveBuffer = null;

                        try
                        {
                            receiveBuffer = _bufferManager.TakeBuffer(1024 * 8);

                            do
                            {
                                _socket.ReceiveTimeout = Math.Min((int)_receiveTimeSpan.TotalMilliseconds, (int)Math.Min((long)int.MaxValue, (long)TcpConnection.CheckTimeout(stopwatch.Elapsed, timeout).TotalMilliseconds));
                                int i = _socket.Receive(receiveBuffer, 0, Math.Min(receiveBuffer.Length, length), SocketFlags.None);
                                if (i == 0) throw new ConnectionException();

                                _receivedByteCount += i;
                                bufferStream.Write(receiveBuffer, 0, i);
                                length -= i;
                            } while (length > 0);
                        }
                        finally
                        {
                            _bufferManager.ReturnBuffer(receiveBuffer);
                        }
                    }
                    catch (Exception e)
                    {
                        bufferStream.Dispose();

                        throw e;
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    return bufferStream;
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

        public override void Send(Stream stream, TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");
            if (!_connect) throw new ConnectionException();

            lock (_sendLock)
            {
                Stopwatch stopwatch = null;

                try
                {
                    stopwatch = new Stopwatch();
                    stopwatch.Start();

                    Stream headerStream = new BufferStream(_bufferManager);
                    headerStream.Write(NetworkConverter.GetBytes((int)stream.Length), 0, 4);

                    byte[] sendBuffer = null;

                    try
                    {
                        sendBuffer = _bufferManager.TakeBuffer(1024 * 8);

                        using (Stream dataStream = new JoinStream(headerStream, new RangeStream(stream, true)))
                        {
                            int i = -1;

                            while (0 < (i = dataStream.Read(sendBuffer, 0, sendBuffer.Length)))
                            {
                                _socket.SendTimeout = Math.Min((int)_sendTimeSpan.TotalMilliseconds, (int)Math.Min((long)int.MaxValue, (long)TcpConnection.CheckTimeout(stopwatch.Elapsed, timeout).TotalMilliseconds));
                                _socket.Send(sendBuffer, 0, i, SocketFlags.None);
                                _sendUpdateTime = DateTime.UtcNow;
                                _sentByteCount += i;
                            }
                        }
                    }
                    finally
                    {
                        _bufferManager.ReturnBuffer(sendBuffer);
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

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                if (_socket != null)
                {
                    try
                    {
                        _socket.Close();
                    }
                    catch (Exception)
                    {

                    }

                    _socket = null;
                }
            }

            _disposed = true;
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Library.Io;

namespace Library.Net.Connection
{
    public class TcpConnection : ConnectionBase, IThisLock
    {
        private Socket _socket;
        private IPEndPoint _remoteEndPoint;
        private int _maxReceiveCount;
        private BufferManager _bufferManager;

        private byte[] _sendBuffer;
        private byte[] _receiveBuffer;
        private GCHandle _sendBufferGCHandle;
        private GCHandle _receiveBufferGCHandle;

        private long _receivedByteCount;
        private long _sentByteCount;

        private bool _disposed = false;

        private object _sendLock = new object();
        private object _receiveLock = new object();
        private object _thisLock = new object();

        private TcpConnection(int maxReceiveCount, BufferManager bufferManager)
        {
            _maxReceiveCount = maxReceiveCount;
            _bufferManager = bufferManager;

            _sendBuffer = _bufferManager.TakeBuffer(1024);
            _sendBufferGCHandle = GCHandle.Alloc(_sendBuffer);
            _receiveBuffer = _bufferManager.TakeBuffer(1024);
            _receiveBufferGCHandle = GCHandle.Alloc(_receiveBuffer);
        }

        public TcpConnection(Socket socket, int maxReceiveCount, BufferManager bufferManager)
            : this(maxReceiveCount, bufferManager)
        {
            _socket = socket;
            //_socket.ReceiveBufferSize = 1024 * 1024;
            //_socket.SendBufferSize = 1024 * 1024;
            //_socket.NoDelay = true;
            //_socket.Blocking = true;
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

            using (DeadlockMonitor.Lock(this.ThisLock))
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
                }
                catch (ConnectionException ex)
                {
                    throw ex;
                }
                catch (Exception ex)
                {
                    throw new ConnectionException(ex.Message, ex);
                }
            }
        }

        public override void Close(TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            using (DeadlockMonitor.Lock(this.ThisLock))
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

            using (DeadlockMonitor.Lock(_receiveLock))
            {
                Stopwatch stopwatch = null;

                try
                {
                    stopwatch = new Stopwatch();
                    stopwatch.Start();

                    byte[] lengthbuffer = new byte[4];
                    _socket.ReceiveTimeout = (int)Math.Min((long)int.MaxValue, (long)TcpConnection.CheckTimeout(stopwatch.Elapsed, timeout).TotalMilliseconds);
                    if (_socket.Receive(lengthbuffer) != lengthbuffer.Length) throw new ConnectionException();
                    _receivedByteCount += 4;
                    int length = NetworkConverter.ToInt32(lengthbuffer);

                    if (length > _maxReceiveCount)
                    {
                        throw new ConnectionException();
                    }

                    BufferStream bufferStream = new BufferStream(_bufferManager);

                    do
                    {
                        _socket.ReceiveTimeout = (int)Math.Min((long)int.MaxValue, (long)TcpConnection.CheckTimeout(stopwatch.Elapsed, timeout).TotalMilliseconds);
                        int i = _socket.Receive(_receiveBuffer, 0, Math.Min(_receiveBuffer.Length, length), SocketFlags.None);
                        if (i == 0) throw new ConnectionException();

                        _receivedByteCount += i;
                        bufferStream.Write(_receiveBuffer, 0, i);
                        length -= i;
                    } while (length > 0);

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    return bufferStream;
                }
                catch (ConnectionException ex)
                {
                    Log.Information(ex);
                    throw ex;
                }
                catch (Exception e)
                {
                    Log.Information(e);
                    throw new ConnectionException(e.Message, e);
                }
            }
        }

        public override void Send(Stream stream, TimeSpan timeout)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");

            using (DeadlockMonitor.Lock(_sendLock))
            {
                Stopwatch stopwatch = null;

                try
                {
                    stopwatch = new Stopwatch();
                    stopwatch.Start();

                    //_socket.SendTimeout = (int)Math.Min((long)int.MaxValue, (long)TcpConnection.CheckTimeout(stopwatch.Elapsed, timeout).TotalMilliseconds);
                    //_socket.Send(NetworkConverter.GetBytes((int)stream.Length));
                    //_sentByteCount += 4;

                    Stream headerStream = new BufferStream(_bufferManager);
                    headerStream.Write(NetworkConverter.GetBytes((int)stream.Length), 0, 4);

                    Stream dataStream = new AddStream(headerStream, stream);

                    int i = -1;

                    while (0 < (i = dataStream.Read(_sendBuffer, 0, _sendBuffer.Length)))
                    {
                        _socket.SendTimeout = (int)Math.Min((long)int.MaxValue, (long)TcpConnection.CheckTimeout(stopwatch.Elapsed, timeout).TotalMilliseconds);
                        _socket.Send(_sendBuffer, 0, i, SocketFlags.None);
                        _sentByteCount += i;
                    }
                }
                catch (ConnectionException ex)
                {
                    Log.Information(ex);
                    throw ex;
                }
                catch (Exception e)
                {
                    Log.Information(e);
                    throw new ConnectionException(e.Message, e);
                }
                //finally
                //{
                //    Debug.WriteLine(string.Format("TcpConnection: Send ({0} {1})", stopwatch.Elapsed, NetworkConverter.ToSizeString(stream.Length)));
                //}
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_socket != null)
                    {
                        try
                        {
                            _socket.Dispose();
                        }
                        catch (Exception)
                        {

                        }

                        _socket = null;
                    }

                    if (_receiveBuffer != null)
                    {
                        try
                        {
                            _bufferManager.ReturnBuffer(_receiveBuffer);
                        }
                        catch (Exception)
                        {

                        }

                        _receiveBuffer = null;
                    }

                    if (_sendBuffer != null)
                    {
                        try
                        {
                            _bufferManager.ReturnBuffer(_sendBuffer);
                        }
                        catch (Exception)
                        {

                        }

                        _sendBuffer = null;
                    }
                }

                if (_receiveBufferGCHandle.IsAllocated) _receiveBufferGCHandle.Free();
                if (_sendBufferGCHandle.IsAllocated) _sendBufferGCHandle.Free();

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

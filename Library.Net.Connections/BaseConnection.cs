using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Library.Io;

namespace Library.Net.Connections
{
    public class BaseConnection : Connection, IThisLock
    {
        private CapBase _cap;
        private BandwidthLimit _bandwidthLimit;
        private int _maxReceiveCount;
        private BufferManager _bufferManager;

        private readonly SafeInteger _receivedByteCount = new SafeInteger();
        private readonly SafeInteger _sentByteCount = new SafeInteger();

        private readonly TimeSpan _sendTimeSpan = new TimeSpan(0, 6, 0);
        private readonly TimeSpan _receiveTimeSpan = new TimeSpan(0, 6, 0);
        private readonly TimeSpan _aliveTimeSpan = new TimeSpan(0, 3, 0);

        private System.Threading.Timer _aliveTimer;
        private Stopwatch _aliveStopwatch = new Stopwatch();

        private Stopwatch _sendStopwatch = new Stopwatch();
        private Stopwatch _receiveStopwatch = new Stopwatch();

        private readonly object _sendLock = new object();
        private readonly object _receiveLock = new object();
        private readonly object _thisLock = new object();

        private volatile bool _connect;

        private volatile bool _disposed;

        public BaseConnection(CapBase cap, BandwidthLimit bandwidthLimit, int maxReceiveCount, BufferManager bufferManager)
        {
            _cap = cap;
            _bandwidthLimit = bandwidthLimit;
            _maxReceiveCount = maxReceiveCount;
            _bufferManager = bufferManager;

            if (_bandwidthLimit != null) _bandwidthLimit.Join(this);

            _aliveTimer = new System.Threading.Timer(this.AliveTimer, null, 1000 * 30, 1000 * 30);
            _aliveStopwatch.Start();

            _connect = true;
        }

        public BandwidthLimit BandwidthLimit
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _bandwidthLimit;
                }
            }
        }

        public override IEnumerable<Connection> GetLayers()
        {
            return new Connection[] { this };
        }

        public override long ReceivedByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return (long)_receivedByteCount;
            }
        }

        public override long SentByteCount
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return (long)_sentByteCount;
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
                if (_cap != null)
                {
                    try
                    {
                        _cap.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _cap = null;
                }

                _connect = false;
            }
        }

        private bool _aliveSending;

        private void AliveTimer(object state)
        {
            if (_disposed) return;
            if (!_connect) return;

            if (_aliveSending) return;
            _aliveSending = true;

            try
            {
                Thread.CurrentThread.Name = "BaseConnection_AliveTimer";

                try
                {
                    if (_aliveStopwatch.Elapsed > _aliveTimeSpan)
                    {
                        this.Alive();
                    }
                }
                catch (Exception)
                {

                }
            }
            finally
            {
                _aliveSending = false;
            }
        }

        private void Alive()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_sendLock)
            {
                byte[] buffer = new byte[4];

                _cap.Send(buffer, 0, buffer.Length, _sendTimeSpan);

                _aliveStopwatch.Restart();
                _sentByteCount.Add(4);
            }
        }

        public override Stream Receive(TimeSpan timeout, Information options)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
            if (!_connect) throw new ConnectionException();

            lock (_receiveLock)
            {
                try
                {
                    _receiveStopwatch.Restart();

                Restart: ;

                    int length = 0;

                    {
                        byte[] lengthbuffer = new byte[4];

                        var time = BaseConnection.CheckTimeout(_receiveStopwatch.Elapsed, timeout);
                        time = (time < _receiveTimeSpan) ? time : _receiveTimeSpan;

                        _cap.Receive(lengthbuffer, time);

                        _receivedByteCount.Add(4);

                        length = NetworkConverter.ToInt32(lengthbuffer);
                    }

                    if (length == 0)
                    {
                        Thread.Sleep(100);
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
                            receiveBuffer = _bufferManager.TakeBuffer(1024 * 4);

                            do
                            {
                                int receiveLength = Math.Min(receiveBuffer.Length, length);

                                if (_bandwidthLimit != null)
                                {
                                    receiveLength = _bandwidthLimit.GetInBandwidth(this, receiveLength);
                                    if (receiveLength < 0) throw new ConnectionException();
                                }

                                var time = BaseConnection.CheckTimeout(_receiveStopwatch.Elapsed, timeout);
                                time = (time < _receiveTimeSpan) ? time : _receiveTimeSpan;

                                _cap.Receive(receiveBuffer, 0, receiveLength, time);

                                _receivedByteCount.Add(receiveLength);
                                bufferStream.Write(receiveBuffer, 0, receiveLength);

                                length -= receiveLength;
                            } while (length > 0);
                        }
                        finally
                        {
                            _bufferManager.ReturnBuffer(receiveBuffer);
                        }
                    }
                    catch (Exception e)
                    {
                        if (bufferStream != null)
                        {
                            bufferStream.Dispose();
                        }

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

        public override void Send(Stream stream, TimeSpan timeout, Information options)
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
                        _sendStopwatch.Restart();

                        Stream headerStream = new BufferStream(_bufferManager);
                        headerStream.Write(NetworkConverter.GetBytes((int)targetStream.Length), 0, 4);

                        byte[] sendBuffer = null;

                        try
                        {
                            sendBuffer = _bufferManager.TakeBuffer(1024 * 4);

                            using (Stream dataStream = new UniteStream(headerStream, new WrapperStream(targetStream, true)))
                            {
                                for (; ; )
                                {
                                    int sendLength = (int)Math.Min(dataStream.Length - dataStream.Position, sendBuffer.Length);
                                    if (sendLength == 0) break;

                                    if (_bandwidthLimit != null)
                                    {
                                        sendLength = _bandwidthLimit.GetOutBandwidth(this, sendLength);
                                        if (sendLength < 0) throw new ConnectionException();
                                    }

                                    dataStream.Read(sendBuffer, 0, sendLength);

                                    var time = BaseConnection.CheckTimeout(_sendStopwatch.Elapsed, timeout);
                                    time = (time < _sendTimeSpan) ? time : _sendTimeSpan;

                                    _cap.Send(sendBuffer, 0, sendLength, time);

                                    _aliveStopwatch.Restart();
                                    _sentByteCount.Add(sendLength);
                                }
                            }
                        }
                        finally
                        {
                            _bufferManager.ReturnBuffer(sendBuffer);
                        }

                        _aliveTimer.Change(1000 * 30);
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
                if (_aliveTimer != null)
                {
                    try
                    {
                        _aliveTimer.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _aliveTimer = null;
                }

                if (_cap != null)
                {
                    try
                    {
                        _cap.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _cap = null;
                }

                if (_bandwidthLimit != null)
                {
                    try
                    {
                        _bandwidthLimit.Leave(this);
                    }
                    catch (Exception)
                    {

                    }

                    _bandwidthLimit = null;
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

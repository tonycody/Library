using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Library.Io;
using Library.Net.Caps;

namespace Library.Net.Connections
{
    public class CapConnection : ConnectionBase, IThisLock
    {
        private CapBase _cap;
        private int _maxReceiveCount;
        private BufferManager _bufferManager;

        private BandwidthLimit _bandwidthLimit;

        private long _receivedByteCount;
        private long _sentByteCount;

        private DateTime _sendUpdateTime;
        private readonly TimeSpan _sendTimeSpan = new TimeSpan(0, 6, 0);
        private readonly TimeSpan _receiveTimeSpan = new TimeSpan(0, 6, 0);
        private readonly TimeSpan _aliveTimeSpan = new TimeSpan(0, 3, 0);

        private System.Threading.Timer _aliveTimer;

        private readonly object _sendLock = new object();
        private readonly object _receiveLock = new object();
        private readonly object _thisLock = new object();

        private volatile bool _connect;
        private volatile bool _disposed;

        public CapConnection(CapBase cap, BandwidthLimit bandwidthLimit, int maxReceiveCount, BufferManager bufferManager)
        {
            _cap = cap;
            _bandwidthLimit = bandwidthLimit;
            _maxReceiveCount = maxReceiveCount;
            _bufferManager = bufferManager;

            if (_bandwidthLimit != null) _bandwidthLimit.Join(this);

            _aliveTimer = new Timer(this.AliveTimer, null, 1000 * 30, 1000 * 30);
            _sendUpdateTime = DateTime.UtcNow;

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

        public override IEnumerable<ConnectionBase> GetLayers()
        {
            return new ConnectionBase[] { this };
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
            throw new NotSupportedException();
        }

        private bool _aliveSending;

        private void AliveTimer(object state)
        {
            try
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);
                if (!_connect) return;

                if (_aliveSending) return;
                _aliveSending = true;

                Thread.CurrentThread.Name = "CapConnection_AliveTimer";

                try
                {
                    if ((DateTime.UtcNow - _sendUpdateTime) > _aliveTimeSpan)
                    {
                        this.Alive();
                    }
                }
                catch (Exception)
                {

                }
                finally
                {
                    _aliveSending = false;
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

                _cap.Send(buffer, 0, buffer.Length, _sendTimeSpan);
                _sendUpdateTime = DateTime.UtcNow;
                _sentByteCount += 4;
            }
        }

        public override void Close(TimeSpan timeout)
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

                    int length = 0;

                    {
                        byte[] lengthbuffer = new byte[4];

                        var time = CapConnection.CheckTimeout(stopwatch.Elapsed, timeout);
                        time = (time < _receiveTimeSpan) ? time : _receiveTimeSpan;

                        if (_cap.Receive(lengthbuffer, time) != lengthbuffer.Length) throw new ConnectionException();
                        _receivedByteCount += 4;

                        length = NetworkConverter.ToInt32(lengthbuffer);
                    }

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
                                int receiveLength = Math.Min(receiveBuffer.Length, length);

                                if (_bandwidthLimit != null)
                                {
                                    receiveLength = _bandwidthLimit.GetInBandwidth(this, receiveLength);
                                    if (receiveLength < 0) throw new ConnectionException();
                                }

                                var time = CapConnection.CheckTimeout(stopwatch.Elapsed, timeout);
                                time = (time < _receiveTimeSpan) ? time : _receiveTimeSpan;

                                int i = _cap.Receive(receiveBuffer, 0, receiveLength, time);

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
                        if (bufferStream != null)
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
            if (!_connect) throw new ConnectionException();
            if (stream == null) throw new ArgumentNullException("stream");
            if (stream.Length == 0) throw new ArgumentOutOfRangeException("stream");

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

                        using (Stream dataStream = new JoinStream(headerStream, new WrapperStream(stream, true)))
                        {
                            int i = -1;

                            for (; ; )
                            {
                                int sendLength = (int)Math.Min(dataStream.Length - dataStream.Position, sendBuffer.Length);
                                if (sendLength <= 0) break;

                                if (_bandwidthLimit != null)
                                {
                                    sendLength = _bandwidthLimit.GetOutBandwidth(this, sendLength);
                                    if (sendLength < 0) throw new ConnectionException();
                                }

                                if ((i = dataStream.Read(sendBuffer, 0, sendLength)) < 0) break;

                                var time = CapConnection.CheckTimeout(stopwatch.Elapsed, timeout);
                                time = (time < _sendTimeSpan) ? time : _sendTimeSpan;

                                _cap.Send(sendBuffer, 0, i, time);
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

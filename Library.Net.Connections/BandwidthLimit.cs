using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml;
using Library.Io;

namespace Library.Net.Connections
{
    public interface IBandwidthLimit
    {
        BandwidthLimit BandwidthLimit { get; set; }
    }

    [DataContract(Name = "BandwidthLimit", Namespace = "http://Library/Net/Connection")]
    public sealed class BandwidthLimit : ManagerBase, IEquatable<BandwidthLimit>, ICloneable<BandwidthLimit>, IThisLock
    {
        private Thread _watchThread;

        private ManualResetEvent _outResetEvent = new ManualResetEvent(false);
        private ManualResetEvent _inResetEvent = new ManualResetEvent(false);

        private long _totalOutSize;
        private long _totalInSize;

        private object _outLockObject = new object();
        private object _inLockObject = new object();

        private HashSet<Connection> _connections = new HashSet<Connection>();
        private object _connectionsLockObject = new object();

        private volatile int _out;
        private volatile int _in;

        private static readonly object _initializeLock = new object();
        private volatile object _thisLock;

        private volatile bool _disposed;

        public BandwidthLimit()
        {
            _watchThread = new Thread(this.WatchThread);
            _watchThread.Priority = ThreadPriority.Highest;
            _watchThread.Name = "BandwidthLimit_WatchThread";
            _watchThread.Start();
        }

        private void WatchThread()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            try
            {
                while (!_disposed)
                {
                    Thread.Sleep(((int)Math.Max(2, 1000 - sw.ElapsedMilliseconds)) / 2);
                    if (sw.ElapsedMilliseconds < 1000) continue;

                    sw.Restart();

                    lock (_outLockObject)
                    {
                        _totalOutSize = 0;
                        _outResetEvent.Set();
                    }

                    lock (_inLockObject)
                    {
                        _totalInSize = 0;
                        _inResetEvent.Set();
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        public static bool operator ==(BandwidthLimit x, BandwidthLimit y)
        {
            if ((object)x == null)
            {
                if ((object)y == null) return true;

                return y.Equals(x);
            }
            else
            {
                return x.Equals(y);
            }
        }

        public static bool operator !=(BandwidthLimit x, BandwidthLimit y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                return (int)(this.In ^ this.Out);
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is BandwidthLimit)) return false;

            return this.Equals((BandwidthLimit)obj);
        }

        public bool Equals(BandwidthLimit other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.In != other.In
                || this.Out != other.Out)
            {
                return false;
            }

            return true;
        }

        internal int GetOutBandwidth(Connection connection, int size)
        {
            for (; ; )
            {
                if (_out == 0) return size;

                lock (_outLockObject)
                {
                    if (_out <= _totalOutSize)
                    {
                        _outResetEvent.Reset();
                    }
                    else if (_out < (_totalOutSize + size))
                    {
                        int s = (int)(_out - _totalOutSize);

                        _totalOutSize += s;

                        return s;
                    }
                    else
                    {
                        _totalOutSize += size;

                        return size;
                    }
                }

                _outResetEvent.WaitOne();

                lock (_connectionsLockObject)
                {
                    if (!_connections.Contains(connection)) return -1;
                }
            }
        }

        internal int GetInBandwidth(Connection connection, int size)
        {
            for (; ; )
            {
                if (_in == 0) return size;

                lock (_inLockObject)
                {
                    if (_in <= _totalInSize)
                    {
                        _inResetEvent.Reset();
                    }
                    else if (_in < (_totalInSize + size))
                    {
                        int s = (int)(_in - _totalInSize);

                        _totalInSize += s;

                        return s;
                    }
                    else
                    {
                        _totalInSize += size;

                        return size;
                    }
                }

                _inResetEvent.WaitOne();

                lock (_connectionsLockObject)
                {
                    if (!_connections.Contains(connection)) return -1;
                }
            }
        }

        internal void Join(Connection connection)
        {
            lock (_connectionsLockObject)
            {
                _connections.Add(connection);
            }
        }

        internal void Leave(Connection connection)
        {
            lock (_connectionsLockObject)
            {
                _connections.Remove(connection);
            }
        }

        [DataMember(Name = "In")]
        public int In
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _in;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _in = value;
                }
            }
        }

        [DataMember(Name = "Out")]
        public int Out
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _out;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _out = value;
                }
            }
        }

        #region ICloneable<BandwidthLimit>

        public BandwidthLimit Clone()
        {
            lock (this.ThisLock)
            {
                var ds = new DataContractSerializer(typeof(BandwidthLimit));

                using (BufferStream stream = new BufferStream(BufferManager.Instance))
                {
                    using (WrapperStream wrapperStream = new WrapperStream(stream, true))
                    using (XmlDictionaryWriter xmlDictionaryWriter = XmlDictionaryWriter.CreateBinaryWriter(wrapperStream))
                    {
                        ds.WriteObject(xmlDictionaryWriter, this);
                    }

                    stream.Position = 0;

                    using (XmlDictionaryReader xmlDictionaryReader = XmlDictionaryReader.CreateBinaryReader(stream, XmlDictionaryReaderQuotas.Max))
                    {
                        return (BandwidthLimit)ds.ReadObject(xmlDictionaryReader);
                    }
                }
            }
        }

        #endregion

        #region IThisLock

        public object ThisLock
        {
            get
            {
                if (_thisLock == null)
                {
                    lock (_initializeLock)
                    {
                        if (_thisLock == null)
                        {
                            _thisLock = new object();
                        }
                    }
                }

                return _thisLock;
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
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

                if (_outResetEvent != null)
                {
                    try
                    {
                        _outResetEvent.Set();
                        _outResetEvent.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _outResetEvent = null;
                }

                if (_inResetEvent != null)
                {
                    try
                    {
                        _inResetEvent.Set();
                        _inResetEvent.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _inResetEvent = null;
                }
            }
        }
    }
}

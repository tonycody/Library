using System;
using System.Collections.Generic;
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
    public sealed class BandwidthLimit : ManagerBase, IDeepCloneable<BandwidthLimit>, IEquatable<BandwidthLimit>, IThisLock
    {
        private System.Timers.Timer _refreshTimer = new System.Timers.Timer();

        private ManualResetEvent _outResetEvent = new ManualResetEvent(false);
        private ManualResetEvent _inResetEvent = new ManualResetEvent(false);

        private long _totalOutSize = 0;
        private long _totalInSize = 0;

        private object _outLockObject = new object();
        private object _inLockObject = new object();

        private HashSet<ConnectionBase> _connections = new HashSet<ConnectionBase>();
        private object _connectionsLockObject = new object();

        private volatile int _out;
        private volatile int _in;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        private volatile bool _disposed = false;

        public BandwidthLimit()
        {
            _refreshTimer = new System.Timers.Timer();
            _refreshTimer.Elapsed += new System.Timers.ElapsedEventHandler(_refreshTimer_Elapsed);
            _refreshTimer.Interval = 1000;
            _refreshTimer.AutoReset = true;
            _refreshTimer.Start();
        }

        private void _refreshTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
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

        public static bool operator ==(BandwidthLimit x, BandwidthLimit y)
        {
            if ((object)x == null)
            {
                if ((object)y == null) return true;

                return ((BandwidthLimit)y).Equals((BandwidthLimit)x);
            }
            else
            {
                return ((BandwidthLimit)x).Equals((BandwidthLimit)y);
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
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.In != other.In
                || this.Out != other.Out)
            {
                return false;
            }

            return true;
        }

        internal int GetOutBandwidth(ConnectionBase connection, int size)
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

                _outResetEvent.WaitOne(3000);

                lock (_connectionsLockObject)
                {
                    if (!_connections.Contains(connection)) return -1;
                }
            }
        }

        internal int GetInBandwidth(ConnectionBase connection, int size)
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

                _inResetEvent.WaitOne(3000);

                lock (_connectionsLockObject)
                {
                    if (!_connections.Contains(connection)) return -1;
                }
            }
        }

        internal void Join(ConnectionBase connection)
        {
            lock (_connectionsLockObject)
            {
                _connections.Add(connection);
            }
        }

        internal void Leave(ConnectionBase connection)
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

        #region IDeepClone<BandwidthLimit>

        public BandwidthLimit DeepClone()
        {
            lock (this.ThisLock)
            {
                var ds = new DataContractSerializer(typeof(BandwidthLimit));

                using (BufferStream stream = new BufferStream(BufferManager.Instance))
                {
                    using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(stream, new UTF8Encoding(false), false))
                    {
                        ds.WriteObject(textDictionaryWriter, this);
                    }

                    stream.Position = 0;

                    using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(stream, XmlDictionaryReaderQuotas.Max))
                    {
                        return (BandwidthLimit)ds.ReadObject(textDictionaryReader);
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
                lock (_thisStaticLock)
                {
                    if (_thisLock == null)
                        _thisLock = new object();

                    return _thisLock;
                }
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_refreshTimer != null)
                {
                    try
                    {
                        _refreshTimer.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _refreshTimer = null;
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml;

namespace Library.Net.Connection
{
    public interface IBandwidthLimit
    {
        BandwidthLimit BandwidthLimit { get; set; }
    }

    [DataContract(Name = "BandwidthLimit", Namespace = "http://Library/Net/Connection")]
    public sealed class BandwidthLimit : IDeepCloneable<BandwidthLimit>, IEquatable<BandwidthLimit>, IThisLock
    {
        private System.Timers.Timer _refreshTimer = new System.Timers.Timer();
        private ManualResetEvent _outManualResetEvent = new ManualResetEvent(false);
        private ManualResetEvent _inManualResetEvent = new ManualResetEvent(false);
        private long _totalOutSize = 0;
        private long _totalInSize = 0;
        private object _outLockObject = new object();
        private object _inLockObject = new object();

        private HashSet<ConnectionBase> _leaveConnection = new HashSet<ConnectionBase>();
        private object _leaveConnectionLockObject = new object();

        private long _out;
        private long _in;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        public BandwidthLimit()
        {
            _refreshTimer = new System.Timers.Timer();
            _refreshTimer.Elapsed += new System.Timers.ElapsedEventHandler(_refreshTimer_Elapsed);
            _refreshTimer.Interval = 1000;
            _refreshTimer.AutoReset = true;
            _refreshTimer.Start();
        }

        void _refreshTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            lock (_outLockObject)
            {
                _totalOutSize = 0;
                _outManualResetEvent.Set();
            }

            lock (_inLockObject)
            {
                _totalInSize = 0;
                _inManualResetEvent.Set();
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

        public int GetOutBandwidth(ConnectionBase connection, int size)
        {
            if (_out == 0) return size;

            for (; ; )
            {
                lock (_outLockObject)
                {
                    if (_out <= _totalOutSize)
                    {
                        _outManualResetEvent.Reset();
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

                _outManualResetEvent.WaitOne();

                lock (_leaveConnectionLockObject)
                {
                    if (_leaveConnection.Remove(connection)) return -1;
                }
            }
        }

        public int GetInBandwidth(ConnectionBase connection, int size)
        {
            if (_in == 0) return size;

            for (; ; )
            {
                lock (_inLockObject)
                {
                    if (_in <= _totalInSize)
                    {
                        _inManualResetEvent.Reset();
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

                _inManualResetEvent.WaitOne();

                lock (_leaveConnectionLockObject)
                {
                    if (_leaveConnection.Remove(connection)) return -1;
                }
            }
        }

        public void Leave(ConnectionBase connection)
        {
            lock (_leaveConnectionLockObject)
            {
                _leaveConnection.Add(connection);
            }
        }

        [DataMember(Name = "In")]
        public long In
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
        public long Out
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

                using (MemoryStream ms = new MemoryStream())
                {
                    using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(ms, new UTF8Encoding(false), false))
                    {
                        ds.WriteObject(textDictionaryWriter, this);
                    }

                    ms.Position = 0;

                    using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(ms, XmlDictionaryReaderQuotas.Max))
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
    }
}

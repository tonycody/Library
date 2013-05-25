using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml;

namespace Library.Net.Connection
{
    [DataContract(Name = "BandwidthLimit", Namespace = "http://Library/Net/Connection")]
    public sealed class BandwidthLimit : IDeepCloneable<BandwidthLimit>, IEquatable<BandwidthLimit>
    {
        private System.Timers.Timer _refreshTimer = new System.Timers.Timer();
        private Random _random = new Random();

        private Dictionary<ConnectionBase, ManualResetEvent> _outResetEvents = new Dictionary<ConnectionBase, ManualResetEvent>();
        private Dictionary<ConnectionBase, ManualResetEvent> _inResetEvents = new Dictionary<ConnectionBase, ManualResetEvent>();

        private volatile int _out;
        private volatile int _in;

        private volatile int _totalOutSize = 0;
        private volatile int _totalInSize = 0;

        private object _outLockObject = new object();
        private object _inLockObject = new object();

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

                foreach (var item in _outResetEvents.Values.Randomize(_random))
                {
                    item.Set();
                }
            }

            lock (_inLockObject)
            {
                _totalInSize = 0;

                foreach (var item in _inResetEvents.Values.Randomize(_random))
                {
                    item.Set();
                }
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
            return (int)(this.In ^ this.Out);
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

        internal void Join(ConnectionBase connection)
        {
            lock (_outLockObject)
            {
                _outResetEvents[connection] = new ManualResetEvent(false);
            }

            lock (_inLockObject)
            {
                _inResetEvents[connection] = new ManualResetEvent(false);
            }
        }

        internal void Leave(ConnectionBase connection)
        {
            lock (_outLockObject)
            {
                ManualResetEvent resetEvent;

                if (_outResetEvents.TryGetValue(connection, out resetEvent))
                {
                    _outResetEvents.Remove(connection);
                    resetEvent.Set();
                }
            }

            lock (_inLockObject)
            {
                ManualResetEvent resetEvent;

                if (_inResetEvents.TryGetValue(connection, out resetEvent))
                {
                    _inResetEvents.Remove(connection);
                    resetEvent.Set();
                }
            }
        }

        internal int GetOutBandwidth(ConnectionBase connection, int size)
        {
            for (; ; )
            {
                if (_out == 0) return size;

                ManualResetEvent resetEvent;

                lock (_outLockObject)
                {
                    if (_outResetEvents.TryGetValue(connection, out resetEvent))
                    {
                        if (_out <= _totalOutSize)
                        {
                            resetEvent.Reset();
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
                }

                if (resetEvent == null) return -1;

                resetEvent.WaitOne(3000);
            }
        }

        internal int GetInBandwidth(ConnectionBase connection, int size)
        {
            for (; ; )
            {
                if (_in == 0) return size;

                ManualResetEvent resetEvent;

                lock (_inLockObject)
                {
                    if (_inResetEvents.TryGetValue(connection, out resetEvent))
                    {
                        if (_in <= _totalInSize)
                        {
                            resetEvent.Reset();
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
                }

                if (resetEvent == null) return -1;

                resetEvent.WaitOne(3000);
            }
        }

        [DataMember(Name = "Out")]
        public int Out
        {
            get
            {
                return _out;
            }
            set
            {
                _out = value;
            }
        }

        [DataMember(Name = "In")]
        public int In
        {
            get
            {
                return _in;
            }
            set
            {
                _in = value;
            }
        }

        #region IDeepClone<BandwidthLimit>

        public BandwidthLimit DeepClone()
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

        #endregion
    }
}

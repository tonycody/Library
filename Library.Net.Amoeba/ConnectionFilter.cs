using System;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "ConnectionType", Namespace = "http://Library/Net/Amoeba")]
    public enum ConnectionType
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "Tcp")]
        Tcp = 1,

        [EnumMember(Value = "Socks4Proxy")]
        Socks4Proxy = 2,

        [EnumMember(Value = "Socks4aProxy")]
        Socks4aProxy = 3,

        [EnumMember(Value = "Socks5Proxy")]
        Socks5Proxy = 4,

        [EnumMember(Value = "HttpProxy")]
        HttpProxy = 5,
    }

    [DataContract(Name = "ConnectionFilter", Namespace = "http://Library/Net/Amoeba")]
    public sealed class ConnectionFilter : IEquatable<ConnectionFilter>, ICloneable<ConnectionFilter>, IThisLock
    {
        private UriCondition _uriCondition;
        private ConnectionType _connectionType;
        private string _proxyUri;
        private string _option;

        private static readonly object _initializeLock = new object();
        private volatile object _thisLock;

        public static bool operator ==(ConnectionFilter x, ConnectionFilter y)
        {
            if ((object)x == null)
            {
                if ((object)y == null) return true;

                return ((ConnectionFilter)y).Equals((ConnectionFilter)x);
            }
            else
            {
                return ((ConnectionFilter)x).Equals((ConnectionFilter)y);
            }
        }

        public static bool operator !=(ConnectionFilter x, ConnectionFilter y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                return (int)this.ConnectionType;
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is ConnectionFilter)) return false;

            return this.Equals((ConnectionFilter)obj);
        }

        public bool Equals(ConnectionFilter other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if ((this.UriCondition != other.UriCondition)
                || (this.ConnectionType != other.ConnectionType)
                || (this.ProxyUri != other.ProxyUri)
                || (this.Option != other.Option))
            {
                return false;
            }

            return true;
        }

        [DataMember(Name = "UriCondition")]
        public UriCondition UriCondition
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _uriCondition;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _uriCondition = value;
                }
            }
        }

        [DataMember(Name = "ConnectionType")]
        public ConnectionType ConnectionType
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _connectionType;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _connectionType = value;
                }
            }
        }

        [DataMember(Name = "ProxyUri")]
        public string ProxyUri
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _proxyUri;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _proxyUri = value;
                }
            }
        }

        [DataMember(Name = "Option")]
        public string Option
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _option;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _option = value;
                }
            }
        }

        #region ICloneable<ConnectionFilter>

        public ConnectionFilter Clone()
        {
            lock (this.ThisLock)
            {
                var ds = new DataContractSerializer(typeof(ConnectionFilter));

                using (BufferStream stream = new BufferStream(BufferManager.Instance))
                {
                    using (WrapperStream wrapperStream = new WrapperStream(stream, true))
                    using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateBinaryWriter(wrapperStream))
                    {
                        ds.WriteObject(textDictionaryWriter, this);
                    }

                    stream.Position = 0;

                    using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateBinaryReader(stream, XmlDictionaryReaderQuotas.Max))
                    {
                        return (ConnectionFilter)ds.ReadObject(textDictionaryReader);
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
    }

    [DataContract(Name = "UriCondition", Namespace = "http://Library/Net/Amoeba")]
    public sealed class UriCondition : IEquatable<UriCondition>, ICloneable<UriCondition>, IThisLock
    {
        private string _value;
        private Regex _regex;

        private static readonly object _initializeLock = new object();
        private volatile object _thisLock;

        public static bool operator ==(UriCondition x, UriCondition y)
        {
            if ((object)x == null)
            {
                if ((object)y == null) return true;

                return ((UriCondition)y).Equals((UriCondition)x);
            }
            else
            {
                return ((UriCondition)x).Equals((UriCondition)y);
            }
        }

        public static bool operator !=(UriCondition x, UriCondition y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                return this.Value.Length;
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is UriCondition)) return false;

            return this.Equals((UriCondition)obj);
        }

        public bool Equals(UriCondition other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Value != other.Value)
            {
                return false;
            }

            return true;
        }

        public bool IsMatch(string uri)
        {
            lock (this.ThisLock)
            {
                return _regex.IsMatch(uri);
            }
        }

        [DataMember(Name = "Value")]
        public string Value
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _value;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    var regex = new Regex(value);

                    _regex = regex;
                    _value = value;
                }
            }
        }

        #region ICloneable<UriCondition>

        public UriCondition Clone()
        {
            lock (this.ThisLock)
            {
                var ds = new DataContractSerializer(typeof(UriCollection));

                using (BufferStream stream = new BufferStream(BufferManager.Instance))
                {
                    using (WrapperStream wrapperStream = new WrapperStream(stream, true))
                    using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateBinaryWriter(wrapperStream))
                    {
                        ds.WriteObject(textDictionaryWriter, this);
                    }

                    stream.Position = 0;

                    using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateBinaryReader(stream, XmlDictionaryReaderQuotas.Max))
                    {
                        return (UriCondition)ds.ReadObject(textDictionaryReader);
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
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.IO;
using System.Xml;

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
    public class ConnectionFilter : IDeepCloneable<ConnectionFilter>, IEquatable<ConnectionFilter>, IThisLock
    {
        private UriCondition _uriCondition;
        private ConnectionType _connectionType;
        private string _proxyUri;
        private object _thisLock;
        private static object _thisStaticLock = new object();

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
            using (DeadlockMonitor.Lock(this.ThisLock))
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
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if ((this.UriCondition != other.UriCondition)
                || (this.ConnectionType != other.ConnectionType)
                || (this.ProxyUri != other.ProxyUri))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _uriCondition;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _connectionType;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _proxyUri;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _proxyUri = value;
                }
            }
        }

        #region IDeepClone<ConnectionFilter> メンバ

        public ConnectionFilter DeepClone()
        {
            var ds = new DataContractSerializer(typeof(ConnectionFilter));

            using (MemoryStream ms = new MemoryStream())
            {
                using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(ms, new UTF8Encoding(false), false))
                {
                    ds.WriteObject(textDictionaryWriter, this);
                }

                ms.Position = 0;

                using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(ms, XmlDictionaryReaderQuotas.Max))
                {
                    return (ConnectionFilter)ds.ReadObject(textDictionaryReader);
                }
            }
        }

        #endregion

        #region IThisLock メンバ

        public object ThisLock
        {
            get
            {
                using (DeadlockMonitor.Lock(_thisStaticLock))
                {
                    if (_thisLock == null) 
                        _thisLock = new object();

                    return _thisLock;
                }
            }
        }

        #endregion
    }

    [DataContract(Name = "UriCondition", Namespace = "http://Library/Net/Amoeba")]
    public class UriCondition : IDeepCloneable<UriCondition>, IEquatable<UriCondition>, IThisLock
    {
        private string _value = null;
        private Regex _regex;
        private object _thisLock;
        private static object _thisStaticLock = new object();

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
            using (DeadlockMonitor.Lock(this.ThisLock))
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
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if ((this.Value != other.Value))
            {
                return false;
            }

            return true;
        }

        public bool IsMatch(string uri)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return _regex.IsMatch(uri);
            }
        }

        [DataMember(Name = "Value")]
        public string Value
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _value;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _value = value;
                    _regex = new Regex(value);
                }
            }
        }

        #region IDeepClone<UriCondition> メンバ

        public UriCondition DeepClone()
        {
            var ds = new DataContractSerializer(typeof(UriCollection));

            using (MemoryStream ms = new MemoryStream())
            {
                using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(ms, new UTF8Encoding(false), false))
                {
                    ds.WriteObject(textDictionaryWriter, this);
                }

                ms.Position = 0;

                using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(ms, XmlDictionaryReaderQuotas.Max))
                {
                    return (UriCondition)ds.ReadObject(textDictionaryReader);
                }
            }
        }

        #endregion

        #region IThisLock メンバ

        public object ThisLock
        {
            get
            {
                using (DeadlockMonitor.Lock(_thisStaticLock))
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

using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "BackgroundDownloadState", Namespace = "http://Library/Net/Amoeba")]
    enum BackgroundDownloadState
    {
        [EnumMember(Value = "Downloading")]
        Downloading = 0,

        [EnumMember(Value = "Decoding")]
        Decoding = 1,

        [EnumMember(Value = "Completed")]
        Completed = 2,

        [EnumMember(Value = "Error")]
        Error = 3,
    }

    [DataContract(Name = "BackgroundDownloadItem", Namespace = "http://Library/Net/Amoeba")]
    [KnownType(typeof(Link))]
    [KnownType(typeof(Store))]
    sealed class BackgroundDownloadItem : IDeepCloneable<BackgroundDownloadItem>, IThisLock
    {
        private BackgroundItemType _type;
        private BackgroundDownloadState _state;

        private Seed _seed;

        private int _rank;
        private Index _index;
        private object _value;

        private IndexCollection _indexes;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        [DataMember(Name = "Type")]
        public BackgroundItemType Type
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _type;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _type = value;
                }
            }
        }

        [DataMember(Name = "State")]
        public BackgroundDownloadState State
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _state;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _state = value;
                }
            }
        }

        [DataMember(Name = "Seed")]
        public Seed Seed
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _seed;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _seed = value;
                }
            }
        }

        [DataMember(Name = "Rank")]
        public int Rank
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _rank;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _rank = value;
                }
            }
        }

        [DataMember(Name = "Index")]
        public Index Index
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _index;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _index = value;
                }
            }
        }

        [DataMember(Name = "Value")]
        public object Value
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
                    _value = value;
                }
            }
        }

        [DataMember(Name = "Indexs")]
        public IndexCollection Indexes
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_indexes == null)
                        _indexes = new IndexCollection();

                    return _indexes;
                }
            }
        }

        public BackgroundDownloadItem DeepClone()
        {
            lock (this.ThisLock)
            {
                var ds = new DataContractSerializer(typeof(BackgroundDownloadItem));

                using (BufferStream stream = new BufferStream(BufferManager.Instance))
                {
                    using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(stream, new UTF8Encoding(false), false))
                    {
                        ds.WriteObject(textDictionaryWriter, this);
                    }

                    stream.Position = 0;

                    using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(stream, XmlDictionaryReaderQuotas.Max))
                    {
                        return (BackgroundDownloadItem)ds.ReadObject(textDictionaryReader);
                    }
                }
            }
        }

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

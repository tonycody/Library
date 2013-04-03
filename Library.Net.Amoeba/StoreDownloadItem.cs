using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "StoreDownloadState", Namespace = "http://Library/Net/Amoeba")]
    public enum StoreDownloadState
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

    [DataContract(Name = "StoreDownloadItem", Namespace = "http://Library/Net/Amoeba")]
    sealed class StoreDownloadItem : IDeepCloneable<StoreDownloadItem>, IThisLock
    {
        private StoreDownloadState _state;
        private int _rank;
        private Seed _seed;
        private Index _index;
        private IndexCollection _indexes;
        private Store _store;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        [DataMember(Name = "State")]
        public StoreDownloadState State
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

        [DataMember(Name = "Store")]
        public Store Store
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _store;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _store = value;
                }
            }
        }

        public StoreDownloadItem DeepClone()
        {
            var ds = new DataContractSerializer(typeof(StoreDownloadItem));

            using (MemoryStream ms = new MemoryStream())
            {
                using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(ms, new UTF8Encoding(false), false))
                {
                    ds.WriteObject(textDictionaryWriter, this);
                }

                ms.Position = 0;

                using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(ms, XmlDictionaryReaderQuotas.Max))
                {
                    return (StoreDownloadItem)ds.ReadObject(textDictionaryReader);
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

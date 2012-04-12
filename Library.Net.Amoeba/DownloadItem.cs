using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using System.IO;
using System.Xml;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "DownloadState", Namespace = "http://Library/Net/Amoeba")]
    public enum DownloadState
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

    [DataContract(Name = "DownloadItem", Namespace = "http://Library/Net/Amoeba")]
    class DownloadItem : IDeepCloneable<DownloadItem>, IThisLock
    {
        private int _priority = 3;
        private DownloadState _state;

        private int _rank;
        private Seed _seed;
        private Index _index;
        private string _path;
        private IndexCollection _indexs;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        [DataMember(Name = "Priority")]
        public int Priority
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _priority;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _priority = value;
                }
            }
        }

        [DataMember(Name = "State")]
        public DownloadState State
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _state;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _state = value;
                }
            }
        }

        [DataMember(Name = "Path")]
        public string Path
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _path;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _path = value;
                }
            }
        }

        [DataMember(Name = "Rank")]
        public int Rank
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _rank;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _seed;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _index;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _index = value;
                }
            }
        }

        [DataMember(Name = "Indexs")]
        public IndexCollection Indexs
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (_indexs == null)
                        _indexs = new IndexCollection();

                    return _indexs;
                }
            }
        }

        public DownloadItem DeepClone()
        {
            var ds = new DataContractSerializer(typeof(DownloadItem));

            using (MemoryStream ms = new MemoryStream())
            {
                using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(ms, new UTF8Encoding(false), false))
                {
                    ds.WriteObject(textDictionaryWriter, this);
                }

                ms.Position = 0;

                using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(ms, XmlDictionaryReaderQuotas.Max))
                {
                    return (DownloadItem)ds.ReadObject(textDictionaryReader);
                }
            }
        }

        #region IThisLock メンバ

        public virtual object ThisLock
        {
            get
            {
                using (DeadlockMonitor.Lock(_thisStaticLock))
                {
                    if (_thisLock == null) _thisLock = new object();
                    return _thisLock;
                }
            }
        }

        #endregion
    }
}

using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "UploadState", Namespace = "http://Library/Net/Amoeba")]
    public enum UploadState
    {
        [EnumMember(Value = "ComputeHash")]
        ComputeHash = 0,

        [EnumMember(Value = "Encoding")]
        Encoding = 1,

        [EnumMember(Value = "ComputeCorrection")]
        ParityEncoding = 2,

        [EnumMember(Value = "Uploading")]
        Uploading = 3,

        [EnumMember(Value = "Completed")]
        Completed = 4,

        [EnumMember(Value = "Error")]
        Error = 5,
    }

    [DataContract(Name = "UploadType", Namespace = "http://Library/Net/Amoeba")]
    enum UploadType
    {
        [EnumMember(Value = "Upload")]
        Upload = 0,

        [EnumMember(Value = "Share")]
        Share = 1,
    }

    [DataContract(Name = "UploadItem", Namespace = "http://Library/Net/Amoeba")]
    sealed class UploadItem : IDeepCloneable<UploadItem>, IThisLock
    {
        private UploadType _type;
        private UploadState _state;
        private int _priority = 3;

        private string _filePath;

        private int _rank;
        private KeyCollection _keys;
        private GroupCollection _groups;
        private int _blockLength;
        private CompressionAlgorithm _compressionAlgorithm;
        private CryptoAlgorithm _cryptoAlgorithm;
        private byte[] _cryptoKey;
        private CorrectionAlgorithm _correctionAlgorithm;
        private HashAlgorithm _hashAlgorithm;
        private DigitalSignature _digitalSignature;
        private Seed _seed;

        private long _encodeBytes;
        private long _encodingBytes;

        private List<Key> _LockedKeys;
        private HashSet<Key> _uploadKeys;
        private HashSet<Key> _uploadedKeys;
        private IndexCollection _indexes;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        [DataMember(Name = "Type")]
        public UploadType Type
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
        public UploadState State
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

        [DataMember(Name = "Priority")]
        public int Priority
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _priority;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _priority = value;
                }
            }
        }

        [DataMember(Name = "FilePath")]
        public string FilePath
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _filePath;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _filePath = value;
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

        [DataMember(Name = "Keys")]
        public KeyCollection Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_keys == null)
                        _keys = new KeyCollection();

                    return _keys;
                }
            }
        }

        [DataMember(Name = "Groups")]
        public GroupCollection Groups
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_groups == null)
                        _groups = new GroupCollection();

                    return _groups;
                }
            }
        }

        [DataMember(Name = "BlockLength")]
        public int BlockLength
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _blockLength;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _blockLength = value;
                }
            }
        }

        [DataMember(Name = "CompressionAlgorithm")]
        public CompressionAlgorithm CompressionAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _compressionAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _compressionAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "CryptoAlgorithm")]
        public CryptoAlgorithm CryptoAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _cryptoAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _cryptoAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "CryptoKey")]
        public byte[] CryptoKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _cryptoKey;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _cryptoKey = value;
                }
            }
        }

        [DataMember(Name = "CorrectionAlgorithm")]
        public CorrectionAlgorithm CorrectionAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _correctionAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _correctionAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "HashAlgorithm")]
        public HashAlgorithm HashAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _hashAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _hashAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "DigitalSignature")]
        public DigitalSignature DigitalSignature
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _digitalSignature;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _digitalSignature = value;
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

        [DataMember(Name = "EncodeBytes")]
        public long EncodeBytes
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _encodeBytes;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _encodeBytes = value;
                }
            }
        }

        [DataMember(Name = "EncodingBytes")]
        public long EncodingBytes
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _encodingBytes;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _encodingBytes = value;
                }
            }
        }

        [DataMember(Name = "LockedKeys")]
        public List<Key> LockedKeys
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_LockedKeys == null)
                        _LockedKeys = new List<Key>();

                    return _LockedKeys;
                }
            }
        }

        [DataMember(Name = "UploadKeys")]
        public HashSet<Key> UploadKeys
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_uploadKeys == null)
                        _uploadKeys = new HashSet<Key>();

                    return _uploadKeys;
                }
            }
        }

        [DataMember(Name = "UploadedKeys")]
        public HashSet<Key> UploadedKeys
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_uploadedKeys == null)
                        _uploadedKeys = new HashSet<Key>();

                    return _uploadedKeys;
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

        public UploadItem DeepClone()
        {
            lock (this.ThisLock)
            {
                var ds = new DataContractSerializer(typeof(UploadItem));

                using (BufferStream stream = new BufferStream(BufferManager.Instance))
                {
                    using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(stream, new UTF8Encoding(false), false))
                    {
                        ds.WriteObject(textDictionaryWriter, this);
                    }

                    stream.Position = 0;

                    using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(stream, XmlDictionaryReaderQuotas.Max))
                    {
                        return (UploadItem)ds.ReadObject(textDictionaryReader);
                    }
                }
            }
        }

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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
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
        ComputeCorrection = 2,

        [EnumMember(Value = "Uploading")]
        Uploading = 3,

        [EnumMember(Value = "Completed")]
        Completed = 4,

        [EnumMember(Value = "Error")]
        Error = 5,
    }

    [DataContract(Name = "UploadType", Namespace = "http://Library/Net/Amoeba")]
    public enum UploadType
    {
        [EnumMember(Value = "Upload")]
        Upload = 0,

        [EnumMember(Value = "Share")]
        Share = 1,
    }

    [DataContract(Name = "UploadItem", Namespace = "http://Library/Net/Amoeba")]
    class UploadItem : IThisLock
    {
        private string _filePath;
        private UploadState _state;
        private UploadType _type;
        private int _priority = 3;
        private int _blockLength;
        private KeyCollection _keys;
        private GroupCollection _groups;
        private int _rank;
        private CompressionAlgorithm _compressionAlgorithm;
        private CryptoAlgorithm _cryptoAlgorithm;
        private byte[] _cryptoKey;
        private CorrectionAlgorithm _correctionAlgorithm;
        private HashAlgorithm _hashAlgorithm;
        private HashSet<Key> _uploadKeys;
        private HashSet<Key> _uploadedKeys;
        private Seed _seed;
        private DigitalSignature _digitalSignature;
        private long _encodeBytes;
        private long _encodingBytes;
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
        public UploadState State
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

        [DataMember(Name = "Type")]
        public UploadType Type
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _type;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _type = value;
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

        [DataMember(Name = "FilePath")]
        public string FilePath
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _filePath;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _filePath = value;
                }
            }
        }

        [DataMember(Name = "CompressionAlgorithm")]
        public CompressionAlgorithm CompressionAlgorithm
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _compressionAlgorithm;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _cryptoAlgorithm;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _cryptoAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "CorrectionAlgorithm")]
        public CorrectionAlgorithm CorrectionAlgorithm
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _correctionAlgorithm;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _hashAlgorithm;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _digitalSignature;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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

        [DataMember(Name = "BlockLength")]
        public int BlockLength
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _blockLength;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _blockLength = value;
                }
            }
        }

        [DataMember(Name = "CryptoKey")]
        public byte[] CryptoKey
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _cryptoKey;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _cryptoKey = value;
                }
            }
        }

        [DataMember(Name = "Keys")]
        public KeyCollection Keys
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (_groups == null)
                        _groups = new GroupCollection();

                    return _groups;
                }
            }
        }

        [DataMember(Name = "UploadKeys")]
        public HashSet<Key> UploadKeys
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (_uploadedKeys == null)
                        _uploadedKeys = new HashSet<Key>();

                    return _uploadedKeys;
                }
            }
        }

        [DataMember(Name = "EncodeBytes")]
        public long EncodeBytes
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _encodeBytes;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
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
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _encodingBytes;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    _encodingBytes = value;
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

        #region IThisLock メンバ

        public virtual object ThisLock
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

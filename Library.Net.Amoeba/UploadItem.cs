using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using Library.Security;
using System.IO;
using System.Xml;

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
    class UploadItem : IDeepCloneable<UploadItem>, IThisLock
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

        [DataMember(Name = "Indexs")]
        public IndexCollection Indexs
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_indexs == null)
                        _indexs = new IndexCollection();

                    return _indexs;
                }
            }
        }

        public UploadItem DeepClone()
        {
            var ds = new DataContractSerializer(typeof(UploadItem));

            using (MemoryStream ms = new MemoryStream())
            {
                using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(ms, new UTF8Encoding(false), false))
                {
                    ds.WriteObject(textDictionaryWriter, this);
                }

                ms.Position = 0;

                using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(ms, XmlDictionaryReaderQuotas.Max))
                {
                    return (UploadItem)ds.ReadObject(textDictionaryReader);
                }
            }
        }

        #region IThisLock

        public virtual object ThisLock
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

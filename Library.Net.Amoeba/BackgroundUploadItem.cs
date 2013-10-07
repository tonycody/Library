using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using Library.Security;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "BackgroundUploadState", Namespace = "http://Library/Net/Amoeba")]
    enum BackgroundUploadState
    {
        [EnumMember(Value = "Encoding")]
        Encoding = 0,

        [EnumMember(Value = "Uploading")]
        Uploading = 1,

        [EnumMember(Value = "Completed")]
        Completed = 2,

        [EnumMember(Value = "Error")]
        Error = 3,
    }

    [DataContract(Name = "BackgroundUploadItem", Namespace = "http://Library/Net/Amoeba")]
    [KnownType(typeof(Link))]
    [KnownType(typeof(Store))]
    sealed class BackgroundUploadItem : IDeepCloneable<BackgroundUploadItem>
    {
        private KeyCollection _keys;
        private GroupCollection _groups;
        private List<Key> _LockedKeys;
        private HashSet<Key> _uploadKeys;
        private HashSet<Key> _uploadedKeys;

        [DataMember(Name = "Type")]
        public BackgroundItemType Type { get; set; }

        [DataMember(Name = "Value")]
        public object Value { get; set; }

        [DataMember(Name = "State")]
        public BackgroundUploadState State { get; set; }

        [DataMember(Name = "Rank")]
        public int Rank { get; set; }

        [DataMember(Name = "CompressionAlgorithm")]
        public CompressionAlgorithm CompressionAlgorithm { get; set; }

        [DataMember(Name = "CryptoAlgorithm")]
        public CryptoAlgorithm CryptoAlgorithm { get; set; }

        [DataMember(Name = "CorrectionAlgorithm")]
        public CorrectionAlgorithm CorrectionAlgorithm { get; set; }

        [DataMember(Name = "HashAlgorithm")]
        public HashAlgorithm HashAlgorithm { get; set; }

        [DataMember(Name = "DigitalSignature")]
        public DigitalSignature DigitalSignature { get; set; }

        [DataMember(Name = "Seed")]
        public Seed Seed { get; set; }

        [DataMember(Name = "BlockLength")]
        public int BlockLength { get; set; }

        [DataMember(Name = "CryptoKey")]
        public byte[] CryptoKey { get; set; }

        [DataMember(Name = "Keys")]
        public KeyCollection Keys
        {
            get
            {
                if (_keys == null)
                    _keys = new KeyCollection();

                return _keys;
            }
        }

        [DataMember(Name = "Groups")]
        public GroupCollection Groups
        {
            get
            {
                if (_groups == null)
                    _groups = new GroupCollection();

                return _groups;
            }
        }

        [DataMember(Name = "UploadKeys")]
        public HashSet<Key> UploadKeys
        {
            get
            {
                if (_uploadKeys == null)
                    _uploadKeys = new HashSet<Key>();

                return _uploadKeys;
            }
        }

        [DataMember(Name = "UploadedKeys")]
        public HashSet<Key> UploadedKeys
        {
            get
            {
                if (_uploadedKeys == null)
                    _uploadedKeys = new HashSet<Key>();

                return _uploadedKeys;
            }
        }

        [DataMember(Name = "LockedKeys")]
        public List<Key> LockedKeys
        {
            get
            {
                if (_LockedKeys == null)
                    _LockedKeys = new List<Key>();

                return _LockedKeys;
            }
        }

        public BackgroundUploadItem DeepClone()
        {
            var ds = new DataContractSerializer(typeof(BackgroundUploadItem));

            using (BufferStream stream = new BufferStream(BufferManager.Instance))
            {
                using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(stream, new UTF8Encoding(false), false))
                {
                    ds.WriteObject(textDictionaryWriter, this);
                }

                stream.Position = 0;

                using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(stream, XmlDictionaryReaderQuotas.Max))
                {
                    return (BackgroundUploadItem)ds.ReadObject(textDictionaryReader);
                }
            }
        }
    }
}

using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

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
    sealed class BackgroundDownloadItem : IDeepCloneable<BackgroundDownloadItem>
    {
        private IndexCollection _indexes;

        [DataMember(Name = "Type")]
        public BackgroundItemType Type { get; set; }

        [DataMember(Name = "State")]
        public BackgroundDownloadState State { get; set; }

        [DataMember(Name = "Rank")]
        public int Rank { get; set; }

        [DataMember(Name = "Seed")]
        public Seed Seed { get; set; }

        [DataMember(Name = "Index")]
        public Index Index { get; set; }

        [DataMember(Name = "Indexs")]
        public IndexCollection Indexes
        {
            get
            {
                if (_indexes == null)
                    _indexes = new IndexCollection();

                return _indexes;
            }
        }

        [DataMember(Name = "Value")]
        public object Value { get; set; }

        public BackgroundDownloadItem DeepClone()
        {
            var ds = new DataContractSerializer(typeof(BackgroundDownloadItem));

            using (MemoryStream ms = new MemoryStream())
            {
                using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(ms, new UTF8Encoding(false), false))
                {
                    ds.WriteObject(textDictionaryWriter, this);
                }

                ms.Position = 0;

                using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(ms, XmlDictionaryReaderQuotas.Max))
                {
                    return (BackgroundDownloadItem)ds.ReadObject(textDictionaryReader);
                }
            }
        }
    }
}

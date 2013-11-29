using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using Library.Collections;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "SearchItem", Namespace = "http://Library/Net/Lair")]
    public class SearchItem : IDeepCloneable<SearchItem>, IThisLock
    {
        private SignatureCollection _trustSignatures;
        private TagCollection _tags;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public SearchItem()
        {

        }

        [DataMember(Name = "TrustSignatures")]
        public SignatureCollection TrustSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_trustSignatures == null)
                        _trustSignatures = new SignatureCollection();

                    return _trustSignatures;
                }
            }
        }

        [DataMember(Name = "Tags")]
        public TagCollection Tags
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_tags == null)
                        _tags = new TagCollection();

                    return _tags;
                }
            }
        }

        #region IDeepClone<SearchItem>

        public SearchItem DeepClone()
        {
            lock (this.ThisLock)
            {
                var ds = new DataContractSerializer(typeof(SearchItem));

                using (BufferStream stream = new BufferStream(BufferManager.Instance))
                {
                    using (XmlDictionaryWriter textDictionaryWriter = XmlDictionaryWriter.CreateTextWriter(stream, new UTF8Encoding(false), false))
                    {
                        ds.WriteObject(textDictionaryWriter, this);
                    }

                    stream.Position = 0;

                    using (XmlDictionaryReader textDictionaryReader = XmlDictionaryReader.CreateTextReader(stream, XmlDictionaryReaderQuotas.Max))
                    {
                        return (SearchItem)ds.ReadObject(textDictionaryReader);
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

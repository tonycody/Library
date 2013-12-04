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
    [DataContract(Name = "Criterion", Namespace = "http://Library/Net/Lair")]
    public class Criterion
    {
        private SignatureCollection _trustSignatures;
        private TagCollection _tags;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public Criterion(IEnumerable<string> trustSignatures, IEnumerable<Tag> tags)
        {
            if (trustSignatures != null) this.ProtectedTrustSignatures.AddRange(trustSignatures);
            if (tags != null) this.ProtectedTags.AddRange(tags);
        }

        private object ThisLock
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

        private volatile ReadOnlyCollection<string> _readOnlyTrustSignatures;

        public IEnumerable<string> TrustSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyTrustSignatures == null)
                        _readOnlyTrustSignatures = new ReadOnlyCollection<string>(this.ProtectedTrustSignatures);

                    return _readOnlyTrustSignatures;
                }
            }
        }

        [DataMember(Name = "TrustSignatures")]
        private SignatureCollection ProtectedTrustSignatures
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

        private volatile ReadOnlyCollection<Tag> _readOnlyTags;

        public IEnumerable<Tag> Tags
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyTags == null)
                        _readOnlyTags = new ReadOnlyCollection<Tag>(this.ProtectedTags);

                    return _readOnlyTags;
                }
            }
        }

        [DataMember(Name = "Tags")]
        private TagCollection ProtectedTags
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
    }
}

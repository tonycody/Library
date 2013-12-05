using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "Criterion", Namespace = "http://Library/Net/Lair")]
    public class Criterion
    {
        private SignatureCollection _trustSignatures;
        private LinkCollection _links;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public Criterion(IEnumerable<string> trustSignatures, IEnumerable<Link> links)
        {
            if (trustSignatures != null) this.ProtectedTrustSignatures.AddRange(trustSignatures);
            if (links != null) this.ProtectedLinks.AddRange(links);
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

        private volatile ReadOnlyCollection<Link> _readOnlyLinks;

        public IEnumerable<Link> Links
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyLinks == null)
                        _readOnlyLinks = new ReadOnlyCollection<Link>(this.ProtectedLinks);

                    return _readOnlyLinks;
                }
            }
        }

        [DataMember(Name = "Links")]
        private LinkCollection ProtectedLinks
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_links == null)
                        _links = new LinkCollection();

                    return _links;
                }
            }
        }
    }
}

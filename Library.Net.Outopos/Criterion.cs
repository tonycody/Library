using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "Criterion", Namespace = "http://Library/Net/Outopos")]
    public class Criterion
    {
        private SignatureCollection _trustSignatures;

        private SectionCollection _sections;
        private WikiCollection _wikis;
        private ChatCollection _chats;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public Criterion(IEnumerable<string> trustSignatures, IEnumerable<Section> sections, IEnumerable<Wiki> wikis, IEnumerable<Chat> chats)
        {
            if (trustSignatures != null) this.ProtectedTrustSignatures.AddRange(trustSignatures);

            if (sections != null) this.ProtectedSections.AddRange(sections);
            if (wikis != null) this.ProtectedWikis.AddRange(wikis);
            if (Chats != null) this.ProtectedChats.AddRange(chats);
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

        private volatile ReadOnlyCollection<Section> _readOnlySections;

        public IEnumerable<Section> Sections
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlySections == null)
                        _readOnlySections = new ReadOnlyCollection<Section>(this.ProtectedSections);

                    return _readOnlySections;
                }
            }
        }

        [DataMember(Name = "Sections")]
        private SectionCollection ProtectedSections
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_sections == null)
                        _sections = new SectionCollection();

                    return _sections;
                }
            }
        }

        private volatile ReadOnlyCollection<Wiki> _readOnlyWikis;

        public IEnumerable<Wiki> Wikis
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyWikis == null)
                        _readOnlyWikis = new ReadOnlyCollection<Wiki>(this.ProtectedWikis);

                    return _readOnlyWikis;
                }
            }
        }

        [DataMember(Name = "Wikis")]
        private WikiCollection ProtectedWikis
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_wikis == null)
                        _wikis = new WikiCollection();

                    return _wikis;
                }
            }
        }

        private volatile ReadOnlyCollection<Chat> _readOnlyChats;

        public IEnumerable<Chat> Chats
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyChats == null)
                        _readOnlyChats = new ReadOnlyCollection<Chat>(this.ProtectedChats);

                    return _readOnlyChats;
                }
            }
        }

        [DataMember(Name = "Chats")]
        private ChatCollection ProtectedChats
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_chats == null)
                        _chats = new ChatCollection();

                    return _chats;
                }
            }
        }
    }
}

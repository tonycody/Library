using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "BroadcastProfileContent", Namespace = "http://Library/Net/Outopos")]
    public sealed class BroadcastProfileContent : ItemBase<BroadcastProfileContent>
    {
        private enum SerializeId : byte
        {
            ExchangePublicKey = 0,
            TrustSignature = 1,
            Wiki = 2,
            Chat = 3,
        }

        private ExchangePublicKey _exchangePublicKey;
        private SignatureCollection _trustSignatures;
        private WikiCollection _wikis;
        private ChatCollection _chats;

        public static readonly int MaxTrustSignatureCount = 1024;
        public static readonly int MaxWikiCount = 256;
        public static readonly int MaxChatCount = 256;

        public BroadcastProfileContent(ExchangePublicKey exchangePublicKey, IEnumerable<string> trustSignatures, IEnumerable<Wiki> wikis, IEnumerable<Chat> chats)
        {
            this.ExchangePublicKey = exchangePublicKey;
            if (trustSignatures != null) this.ProtectedTrustSignatures.AddRange(trustSignatures);
            if (wikis != null) this.ProtectedWikis.AddRange(wikis);
            if (chats != null) this.ProtectedChats.AddRange(chats);
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            byte[] lengthBuffer = new byte[4];

            for (; ; )
            {
                if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                int length = NetworkConverter.ToInt32(lengthBuffer);
                byte id = (byte)stream.ReadByte();

                using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                {
                    if (id == (byte)SerializeId.ExchangePublicKey)
                    {
                        this.ExchangePublicKey = ExchangePublicKey.Import(rangeStream, bufferManager);
                    }
                    else if (id == (byte)SerializeId.TrustSignature)
                    {
                        this.ProtectedTrustSignatures.Add(ItemUtilities.GetString(rangeStream));
                    }
                    else if (id == (byte)SerializeId.Wiki)
                    {
                        this.ProtectedWikis.Add(Wiki.Import(rangeStream, bufferManager));
                    }
                    else if (id == (byte)SerializeId.Chat)
                    {
                        this.ProtectedChats.Add(Chat.Import(rangeStream, bufferManager));
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            BufferStream bufferStream = new BufferStream(bufferManager);

            // ExchangePublicKey
            if (this.ExchangePublicKey != null)
            {
                using (var stream = this.ExchangePublicKey.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.ExchangePublicKey, stream);
                }
            }
            // TrustSignatures
            foreach (var value in this.TrustSignatures)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.TrustSignature, value);
            }
            // Wikis
            foreach (var value in this.Wikis)
            {
                using (var stream = value.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Wiki, stream);
                }
            }
            // Chats
            foreach (var value in this.Chats)
            {
                using (var stream = value.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Chat, stream);
                }
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
        }

        public override int GetHashCode()
        {
            if (this.ExchangePublicKey == null) return 0;
            else return this.ExchangePublicKey.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is BroadcastProfileContent)) return false;

            return this.Equals((BroadcastProfileContent)obj);
        }

        public override bool Equals(BroadcastProfileContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.ExchangePublicKey != other.ExchangePublicKey
                || (this.TrustSignatures == null) != (other.TrustSignatures == null)
                || (this.Wikis == null) != (other.Wikis == null)
                || (this.Chats == null) != (other.Chats == null))
            {
                return false;
            }

            if (this.TrustSignatures != null && other.TrustSignatures != null)
            {
                if (!CollectionUtilities.Equals(this.TrustSignatures, other.TrustSignatures)) return false;
            }

            if (this.Wikis != null && other.Wikis != null)
            {
                if (!CollectionUtilities.Equals(this.Wikis, other.Wikis)) return false;
            }

            if (this.Chats != null && other.Chats != null)
            {
                if (!CollectionUtilities.Equals(this.Chats, other.Chats)) return false;
            }

            return true;
        }

        [DataMember(Name = "ExchangePublicKey")]
        public ExchangePublicKey ExchangePublicKey
        {
            get
            {
                return _exchangePublicKey;
            }
            private set
            {
                _exchangePublicKey = value;
            }
        }

        private volatile ReadOnlyCollection<string> _readOnlyTrustSignatures;

        public IEnumerable<string> TrustSignatures
        {
            get
            {
                if (_readOnlyTrustSignatures == null)
                    _readOnlyTrustSignatures = new ReadOnlyCollection<string>(this.ProtectedTrustSignatures);

                return _readOnlyTrustSignatures;
            }
        }

        [DataMember(Name = "TrustSignatures")]
        private SignatureCollection ProtectedTrustSignatures
        {
            get
            {
                if (_trustSignatures == null)
                    _trustSignatures = new SignatureCollection(BroadcastProfileContent.MaxTrustSignatureCount);

                return _trustSignatures;
            }
        }

        private volatile ReadOnlyCollection<Wiki> _readOnlyWikis;

        public IEnumerable<Wiki> Wikis
        {
            get
            {
                if (_readOnlyWikis == null)
                    _readOnlyWikis = new ReadOnlyCollection<Wiki>(this.ProtectedWikis);

                return _readOnlyWikis;
            }
        }

        [DataMember(Name = "Wikis")]
        private WikiCollection ProtectedWikis
        {
            get
            {
                if (_wikis == null)
                    _wikis = new WikiCollection(BroadcastProfileContent.MaxWikiCount);

                return _wikis;
            }
        }

        private volatile ReadOnlyCollection<Chat> _readOnlyChats;

        public IEnumerable<Chat> Chats
        {
            get
            {
                if (_readOnlyChats == null)
                    _readOnlyChats = new ReadOnlyCollection<Chat>(this.ProtectedChats);

                return _readOnlyChats;
            }
        }

        [DataMember(Name = "Chats")]
        private ChatCollection ProtectedChats
        {
            get
            {
                if (_chats == null)
                    _chats = new ChatCollection(BroadcastProfileContent.MaxChatCount);

                return _chats;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "SectionProfileContent", Namespace = "http://Library/Net/Lair")]
    public sealed class SectionProfileContent : ItemBase<SectionProfileContent>, ISectionProfileContent<Document, Chat>
    {
        private enum SerializeId : byte
        {
            Comment = 0,
            ExchangePublicKey = 1,
            TrustSignature = 2,
            Document = 3,
            Chat = 4,
        }

        private string _comment;
        private ExchangePublicKey _exchangePublicKey;
        private SignatureCollection _trustSignatures;
        private DocumentCollection _documents;
        private ChatCollection _chats;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxCommentLength = 1024 * 4;
        public static readonly int MaxTrustSignaturesCount = 1024;
        public static readonly int MaxDocumentsCount = 256;
        public static readonly int MaxChatsCount = 256;

        public static readonly int MaxPublickeyLength = 1024 * 8;

        public SectionProfileContent(string comment, ExchangePublicKey publicKey, IEnumerable<string> trustSignatures,
            IEnumerable<Document> documents, IEnumerable<Chat> chats)
        {
            this.Comment = comment;
            this.ExchangePublicKey = publicKey;
            if (trustSignatures != null) this.ProtectedTrustSignatures.AddRange(trustSignatures);
            if (documents != null) this.ProtectedDocuments.AddRange(documents);
            if (chats != null) this.ProtectedChats.AddRange(chats);
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
        {
            lock (this.ThisLock)
            {
                Encoding encoding = new UTF8Encoding(false);
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Comment)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Comment = reader.ReadToEnd();
                            }
                        }
                        else if (id == (byte)SerializeId.ExchangePublicKey)
                        {
                            this.ExchangePublicKey = ExchangePublicKey.Import(rangeStream, bufferManager);
                        }
                        else if (id == (byte)SerializeId.TrustSignature)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.ProtectedTrustSignatures.Add(reader.ReadToEnd());
                            }
                        }
                        else if (id == (byte)SerializeId.Document)
                        {
                            this.ProtectedDocuments.Add(Document.Import(rangeStream, bufferManager));
                        }
                        else if (id == (byte)SerializeId.Chat)
                        {
                            this.ProtectedChats.Add(Chat.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            lock (this.ThisLock)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Comment
                if (this.Comment != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.Comment);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Comment);

                    streams.Add(bufferStream);
                }
                // PublicKey
                if (this.ExchangePublicKey != null)
                {
                    Stream exportStream = this.ExchangePublicKey.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.ExchangePublicKey);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }
                // TrustSignatures
                foreach (var t in this.TrustSignatures)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(t);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.TrustSignature);

                    streams.Add(bufferStream);
                }
                // Documents
                foreach (var d in this.Documents)
                {
                    Stream exportStream = d.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Document);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }
                // Chats
                foreach (var c in this.Chats)
                {
                    Stream exportStream = c.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Chat);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }

                return new JoinStream(streams);
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.Comment == null) return 0;
                else return this.Comment.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is SectionProfileContent)) return false;

            return this.Equals((SectionProfileContent)obj);
        }

        public override bool Equals(SectionProfileContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Comment != other.Comment
                || this.ExchangePublicKey != other.ExchangePublicKey
                || (this.TrustSignatures == null) != (other.TrustSignatures == null)
                || (this.Documents == null) != (other.Documents == null)
                || (this.Chats == null) != (other.Chats == null))
            {
                return false;
            }

            if (this.TrustSignatures != null && other.TrustSignatures != null)
            {
                if (!Collection.Equals(this.TrustSignatures, other.TrustSignatures)) return false;
            }

            if (this.Documents != null && other.Documents != null)
            {
                if (!Collection.Equals(this.Documents, other.Documents)) return false;
            }

            if (this.Chats != null && other.Chats != null)
            {
                if (!Collection.Equals(this.Chats, other.Chats)) return false;
            }

            return true;
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

        #region ISectionProfile<Tag>

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _comment;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {

                    if (value != null && value.Length > SectionProfileContent.MaxCommentLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _comment = value;
                    }
                }
            }
        }

        [DataMember(Name = "ExchangePublicKey")]
        public ExchangePublicKey ExchangePublicKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _exchangePublicKey;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    _exchangePublicKey = value;
                }
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
                        _trustSignatures = new SignatureCollection(SectionProfileContent.MaxTrustSignaturesCount);

                    return _trustSignatures;
                }
            }
        }

        private volatile ReadOnlyCollection<Document> _readOnlyDocuments;

        public IEnumerable<Document> Documents
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyDocuments == null)
                        _readOnlyDocuments = new ReadOnlyCollection<Document>(this.ProtectedDocuments);

                    return _readOnlyDocuments;
                }
            }
        }

        [DataMember(Name = "Documents")]
        private DocumentCollection ProtectedDocuments
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_documents == null)
                        _documents = new DocumentCollection(SectionProfileContent.MaxDocumentsCount);

                    return _documents;
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
                        _chats = new ChatCollection(SectionProfileContent.MaxChatsCount);

                    return _chats;
                }
            }
        }

        #endregion
    }
}

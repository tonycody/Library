using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "SectionProfile", Namespace = "http://Library/Net/Lair")]
    public sealed class SectionProfile : ItemBase<SectionProfile>, ISectionProfile<Tag>
    {
        private enum SerializeId : byte
        {
            Comment = 0,
            TrustSignature = 1,
            Document = 2,
            Chat = 3,

            ExchangeAlgorithm = 4,
            PublicKey = 5,
        }

        private string _comment;
        private SignatureCollection _trustSignatures = null;
        private KeyCollection _documents = null;
        private KeyCollection _chats = null;

        private ExchangeAlgorithm _exchangeAlgorithm;
        private byte[] _publicKey;

        private int _hashCode = 0;

        public static readonly int MaxCommentLength = 1024 * 4;
        public static readonly int MaxTrustSignaturesCount = 1024;
        public static readonly int MaxDocumentsCount = 1024;
        public static readonly int MaxChatsCount = 1024;

        public static readonly int MaxPublickeyLength = 1024 * 8;

        public SectionProfile(string comment, IEnumerable<string> trustSignatures, IEnumerable<Key> documents, IEnumerable<Key> chats, IExchangeEncrypt exchangeEncrypt)
        {
            this.Comment = comment;
            if (trustSignatures != null) this.ProtectedTrustSignatures.AddRange(trustSignatures);
            if (documents != null) this.ProtectedDocuments.AddRange(documents);
            if (chats != null) this.ProtectedChats.AddRange(chats);

            this.ExchangeAlgorithm = exchangeEncrypt.ExchangeAlgorithm;
            this.PublicKey = exchangeEncrypt.PublicKey;
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
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
                    else if (id == (byte)SerializeId.TrustSignature)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.ProtectedTrustSignatures.Add(reader.ReadToEnd());
                        }
                    }
                    else if (id == (byte)SerializeId.Document)
                    {
                        this.ProtectedDocuments.Add(Key.Import(rangeStream, bufferManager));
                    }
                    else if (id == (byte)SerializeId.Chat)
                    {
                        this.ProtectedChats.Add(Key.Import(rangeStream, bufferManager));
                    }

                    else if (id == (byte)SerializeId.ExchangeAlgorithm)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.ExchangeAlgorithm = (ExchangeAlgorithm)Enum.Parse(typeof(ExchangeAlgorithm), reader.ReadToEnd());
                        }
                    }
                    else if (id == (byte)SerializeId.PublicKey)
                    {
                        byte[] buffer = new byte[(int)rangeStream.Length];
                        rangeStream.Read(buffer, 0, buffer.Length);

                        this.PublicKey = buffer;
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
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
            foreach (var d in this.ProtectedDocuments)
            {
                Stream exportStream = d.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Document);

                streams.Add(new JoinStream(bufferStream, exportStream));
            }
            // Chats
            foreach (var c in this.ProtectedChats)
            {
                Stream exportStream = c.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Chat);

                streams.Add(new JoinStream(bufferStream, exportStream));
            }

            // ExchangeAlgorithm
            if (this.ExchangeAlgorithm != 0)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(this.ExchangeAlgorithm.ToString());
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.ExchangeAlgorithm);

                streams.Add(bufferStream);
            }
            // PublicKey
            if (this.PublicKey != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)this.PublicKey.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.PublicKey);
                bufferStream.Write(this.PublicKey, 0, this.PublicKey.Length);

                streams.Add(bufferStream);
            }

            return new JoinStream(streams);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is SectionProfile)) return false;

            return this.Equals((SectionProfile)obj);
        }

        public override bool Equals(SectionProfile other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Comment != other.Comment
                || (this.TrustSignatures == null) != (other.TrustSignatures == null)
                || (this.Documents == null) != (other.Documents == null)
                || (this.Chats == null) != (other.Chats == null)

                || this.ExchangeAlgorithm != other.ExchangeAlgorithm
                || ((this.PublicKey == null) != (other.PublicKey == null)))
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

            if (this.PublicKey != null && other.PublicKey != null)
            {
                if (!Collection.Equals(this.PublicKey, other.PublicKey)) return false;
            }

            return true;
        }

        public override SectionProfile DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return SectionProfile.Import(stream, BufferManager.Instance);
            }
        }

        #region IProfileContent<Document, Chat>

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                return _comment;
            }
            private set
            {
                if (value != null && value.Length > SectionProfile.MaxCommentLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _comment = value;
                }
            }
        }

        public IEnumerable<string> TrustSignatures
        {
            get
            {
                return this.ProtectedTrustSignatures;
            }
        }

        [DataMember(Name = "TrustSignatures")]
        private SignatureCollection ProtectedTrustSignatures
        {
            get
            {
                if (_trustSignatures == null)
                    _trustSignatures = new SignatureCollection(SectionProfile.MaxTrustSignaturesCount);

                return _trustSignatures;
            }
        }

        public IEnumerable<Key> Documents
        {
            get
            {
                return this.ProtectedDocuments;
            }
        }

        [DataMember(Name = "Documents")]
        private KeyCollection ProtectedDocuments
        {
            get
            {
                if (_documents == null)
                    _documents = new KeyCollection(SectionProfile.MaxDocumentsCount);

                return _documents;
            }
        }

        public IEnumerable<Key> Chats
        {
            get
            {
                return this.ProtectedChats;
            }
        }

        [DataMember(Name = "Chats")]
        private KeyCollection ProtectedChats
        {
            get
            {
                if (_chats == null)
                    _chats = new KeyCollection(SectionProfile.MaxChatsCount);

                return _chats;
            }
        }

        #endregion

        #region  IExchangeEncrypt

        [DataMember(Name = "ExchangeAlgorithm")]
        public ExchangeAlgorithm ExchangeAlgorithm
        {
            get
            {
                return _exchangeAlgorithm;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(ExchangeAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _exchangeAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "PublicKey")]
        public byte[] PublicKey
        {
            get
            {
                return _publicKey;
            }
            private set
            {
                if (value != null && (value.Length > Exchange.MaxPublickeyLength))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _publicKey = value;
                }

                if (value != null && value.Length != 0)
                {
                    _hashCode = BitConverter.ToInt32(Crc32_Castagnoli.ComputeHash(value), 0) & 0x7FFFFFFF;
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }

        #endregion
    }
}

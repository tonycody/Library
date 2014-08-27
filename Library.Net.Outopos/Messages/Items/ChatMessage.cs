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
    [DataContract(Name = "ChatMessage", Namespace = "http://Library/Net/Outopos")]
    sealed class ChatMessage : ImmutableCertificateItemBase<ChatMessage>, IChatMessage
    {
        private enum SerializeId : byte
        {
            Tag = 0,
            CreationTime = 1,
            Comment = 2,
            Anchor = 3,
        }

        private Chat _tag;
        private DateTime _creationTime;
        private string _comment;
        private AnchorCollection _anchors;

        private Certificate _certificate;

        private volatile object _thisLock;

        public static readonly int MaxCommentLength = 1024 * 4;
        public static readonly int MaxAnchorCount = 32;

        public ChatMessage(Chat tag, DateTime creationTime, string comment, IEnumerable<Anchor> anchors)
        {
            this.Tag = tag;
            this.CreationTime = creationTime;
            this.Comment = comment;
            if (anchors != null) this.ProtectedAnchors.AddRange(anchors);
        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (_thisLock)
            {
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Tag)
                        {
                            this.Tag = Chat.Import(rangeStream, bufferManager);
                        }
                        else if (id == (byte)SerializeId.CreationTime)
                        {
                            this.CreationTime = DateTime.ParseExact(ItemUtilities.GetString(rangeStream), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                        }
                        else if (id == (byte)SerializeId.Comment)
                        {
                            this.Comment = ItemUtilities.GetString(rangeStream);
                        }
                        else if (id == (byte)SerializeId.Anchor)
                        {
                            this.ProtectedAnchors.Add(Anchor.Import(rangeStream, bufferManager));
                        }
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            lock (_thisLock)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);

                // Tag
                if (this.Tag != null)
                {
                    using (var stream = this.Tag.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Tag, stream);
                    }
                }
                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.CreationTime, this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
                }
                // Comment
                if (this.Comment != null)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.Comment, this.Comment);
                }
                // Anchors
                foreach (var value in this.Anchors)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Anchor, stream);
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }
        }

        public override int GetHashCode()
        {
            lock (_thisLock)
            {
                if (this.Comment == null) return 0;
                else return this.Comment.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is ChatMessage)) return false;

            return this.Equals((ChatMessage)obj);
        }

        public override bool Equals(ChatMessage other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Tag != other.Tag
                || this.CreationTime != other.CreationTime
                || this.Comment != other.Comment
                || (this.Anchors == null) != (other.Anchors == null))
            {
                return false;
            }

            if (this.Anchors != null && other.Anchors != null)
            {
                if (!CollectionUtilities.Equals(this.Anchors, other.Anchors)) return false;
            }

            return true;
        }

        protected override void CreateCertificate(DigitalSignature digitalSignature)
        {
            lock (_thisLock)
            {
                base.CreateCertificate(digitalSignature);
            }
        }

        public override bool VerifyCertificate()
        {
            lock (_thisLock)
            {
                return base.VerifyCertificate();
            }
        }

        protected override Stream GetCertificateStream()
        {
            lock (_thisLock)
            {
                var temp = this.Certificate;
                this.Certificate = null;

                try
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        stream.Seek(0, SeekOrigin.End);
                        ItemUtilities.Write(stream, byte.MaxValue, "ChatMessage");
                        stream.Seek(0, SeekOrigin.Begin);

                        return this.Export(BufferManager.Instance);
                    }
                }
                finally
                {
                    this.Certificate = temp;
                }
            }
        }

        public override Certificate Certificate
        {
            get
            {
                lock (_thisLock)
                {
                    return _certificate;
                }
            }
            protected set
            {
                lock (_thisLock)
                {
                    _certificate = value;
                }
            }
        }

        [DataMember(Name = "Tag")]
        public Chat Tag
        {
            get
            {
                lock (_thisLock)
                {
                    return _tag;
                }
            }
            private set
            {
                lock (_thisLock)
                {
                    _tag = value;
                }
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                lock (_thisLock)
                {
                    return _creationTime;
                }
            }
            private set
            {
                lock (_thisLock)
                {
                    var utc = value.ToUniversalTime();
                    _creationTime = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc);
                }
            }
        }

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                lock (_thisLock)
                {
                    return _comment;
                }
            }
            private set
            {
                lock (_thisLock)
                {
                    if (value != null && value.Length > ChatMessage.MaxCommentLength)
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

        private volatile ReadOnlyCollection<Anchor> _readOnlyAnchors;

        public IEnumerable<Anchor> Anchors
        {
            get
            {
                lock (_thisLock)
                {
                    if (_readOnlyAnchors == null)
                        _readOnlyAnchors = new ReadOnlyCollection<Anchor>(this.ProtectedAnchors.ToArray());

                    return _readOnlyAnchors;
                }
            }
        }

        [DataMember(Name = "Anchors")]
        private AnchorCollection ProtectedAnchors
        {
            get
            {
                lock (_thisLock)
                {
                    if (_anchors == null)
                        _anchors = new AnchorCollection(ChatMessage.MaxAnchorCount);

                    return _anchors;
                }
            }
        }
    }
}

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
    [DataContract(Name = "ChatMessageContent", Namespace = "http://Library/Net/Outopos")]
    public sealed class ChatMessageContent : ItemBase<ChatMessageContent>
    {
        private enum SerializeId : byte
        {
            Comment = 0,
            Anchor = 1,
        }

        private string _comment;
        private AnchorCollection _anchors;

        public static readonly int MaxCommentLength = 1024 * 4;
        public static readonly int MaxAnchorCount = 32;

        public ChatMessageContent(string comment, IEnumerable<Anchor> anchors)
        {
            this.Comment = comment;
            if (anchors != null) this.ProtectedAnchors.AddRange(anchors);
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
                    if (id == (byte)SerializeId.Comment)
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

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            BufferStream bufferStream = new BufferStream(bufferManager);

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

        public override int GetHashCode()
        {
            if (this.Comment == null) return 0;
            else return this.Comment.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is ChatMessageContent)) return false;

            return this.Equals((ChatMessageContent)obj);
        }

        public override bool Equals(ChatMessageContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Comment != other.Comment
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

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                return _comment;
            }
            private set
            {
                if (value != null && value.Length > ChatMessageContent.MaxCommentLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _comment = value;
                }
            }
        }

        private volatile ReadOnlyCollection<Anchor> _readOnlyAnchors;

        public IEnumerable<Anchor> Anchors
        {
            get
            {
                if (_readOnlyAnchors == null)
                    _readOnlyAnchors = new ReadOnlyCollection<Anchor>(this.ProtectedAnchors.ToArray());

                return _readOnlyAnchors;
            }
        }

        [DataMember(Name = "Anchors")]
        private AnchorCollection ProtectedAnchors
        {
            get
            {
                if (_anchors == null)
                    _anchors = new AnchorCollection(ChatMessageContent.MaxAnchorCount);

                return _anchors;
            }
        }
    }
}

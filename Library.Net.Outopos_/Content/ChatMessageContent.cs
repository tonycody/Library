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
            AnchorSignature = 1,
        }

        private string _comment;
        private SignatureCollection _anchorSignatures;

        public static readonly int MaxCommentLength = 1024 * 4;
        public static readonly int MaxAnchorSignatureCount = 32;

        public ChatMessageContent(string comment, IEnumerable<string> anchorSignatrues)
        {
            this.Comment = comment;
            if (anchorSignatrues != null) this.ProtectedAnchorSignatures.AddRange(anchorSignatrues);
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
                    else if (id == (byte)SerializeId.AnchorSignature)
                    {
                        this.ProtectedAnchorSignatures.Add(ItemUtilities.GetString(rangeStream));
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
            // AnchorSignatures
            foreach (var value in this.AnchorSignatures)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.AnchorSignature, value);
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
                || (this.AnchorSignatures == null) != (other.AnchorSignatures == null))
            {
                return false;
            }

            if (this.AnchorSignatures != null && other.AnchorSignatures != null)
            {
                if (!CollectionUtilities.Equals(this.AnchorSignatures, other.AnchorSignatures)) return false;
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

        private volatile ReadOnlyCollection<string> _readOnlySignatureAnchors;

        public IEnumerable<string> AnchorSignatures
        {
            get
            {
                if (_readOnlySignatureAnchors == null)
                    _readOnlySignatureAnchors = new ReadOnlyCollection<string>(this.ProtectedAnchorSignatures);

                return _readOnlySignatureAnchors;
            }
        }

        [DataMember(Name = "AnchorSignatures")]
        private SignatureCollection ProtectedAnchorSignatures
        {
            get
            {
                if (_anchorSignatures == null)
                    _anchorSignatures = new SignatureCollection(ChatMessageContent.MaxAnchorSignatureCount);

                return _anchorSignatures;
            }
        }
    }
}

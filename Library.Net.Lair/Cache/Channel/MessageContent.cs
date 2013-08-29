using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "MessageContent", Namespace = "http://Library/Net/Lair")]
    public sealed class MessageContent : ItemBase<MessageContent>, IMessageContent<Key>
    {
        private enum SerializeId : byte
        {
            Comment = 0,
            Anchor = 1,
        }

        private string _comment = null;
        private KeyCollection _anchors = null;

        public static readonly int MaxCommentLength = 1024 * 4;
        public static readonly int MaxAnchorsCount = 32;

        public MessageContent(string comment, IEnumerable<Key> anchors)
        {
            this.Comment = comment;
            if (anchors != null) this.ProtectedAnchors.AddRange(anchors);
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
                    else if (id == (byte)SerializeId.Anchor)
                    {
                        this.ProtectedAnchors.Add(Key.Import(rangeStream, bufferManager));
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
            // Anchors
            foreach (var a in this.Anchors)
            {
                Stream exportStream = a.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Anchor);

                streams.Add(new JoinStream(bufferStream, exportStream));
            }

            return new JoinStream(streams);
        }

        public override int GetHashCode()
        {
            if (_comment == null) return 0;
            else return _comment.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is MessageContent)) return false;

            return this.Equals((MessageContent)obj);
        }

        public override bool Equals(MessageContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Comment != other.Comment
                || (this.Anchors == null) != (other.Anchors == null))
            {
                return false;
            }

            if (this.Anchors != null && other.Anchors != null)
            {
                if (!Collection.Equals(this.Anchors, other.Anchors)) return false;
            }

            return true;
        }

        public override string ToString()
        {
            return this.Comment;
        }

        public override MessageContent DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return MessageContent.Import(stream, BufferManager.Instance);
            }
        }

        #region IMessageContent<Key>

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                return _comment;
            }
            private set
            {
                if (value != null && value.Length > MessageContent.MaxCommentLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _comment = value;
                }
            }
        }

        public IEnumerable<Key> Anchors
        {
            get
            {
                return this.ProtectedAnchors;
            }
        }

        [DataMember(Name = "Anchors")]
        private KeyCollection ProtectedAnchors
        {
            get
            {
                if (_anchors == null)
                    _anchors = new KeyCollection(MessageContent.MaxAnchorsCount);

                return _anchors;
            }
        }

        #endregion
    }
}

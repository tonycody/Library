using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "SectionMessage", Namespace = "http://Library/Net/Lair")]
    public sealed class SectionMessage : ItemBase<SectionMessage>, ISectionMessage<Key>
    {
        private enum SerializeId : byte
        {
            Comment = 0,
            Anchor = 1,
        }

        private string _comment = null;
        private Key _anchor = null;

        private int _hashCode = 0;

        public static readonly int MaxCommentLength = 1024 * 4;

        public SectionMessage(string comment, Key anchor)
        {
            this.Comment = comment;
            this.Anchor = anchor;
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
                        this.Anchor = Key.Import(rangeStream, bufferManager);
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
            // Anchor
            if (this.Anchor != null)
            {
                Stream exportStream = this.Anchor.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Anchor);

                streams.Add(new JoinStream(bufferStream, exportStream));
            }

            return new JoinStream(streams);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is SectionMessage)) return false;

            return this.Equals((SectionMessage)obj);
        }

        public override bool Equals(SectionMessage other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Comment != other.Comment
                || this.Anchor != other.Anchor)
            {
                return false;
            }

            return true;
        }

        public override SectionMessage DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return SectionMessage.Import(stream, BufferManager.Instance);
            }
        }

        #region ISectionMessageContent

        [DataMember(Name = "Comment")]
        public string Comment
        {
            get
            {
                return _comment;
            }
            private set
            {
                if (value != null && value.Length > SectionMessage.MaxCommentLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _comment = value;
                }
            }
        }

        [DataMember(Name = "Anchor")]
        public Key Anchor
        {
            get
            {
                return _anchor;
            }
            private set
            {
                _anchor = value;

                if (_anchor == null)
                {
                    _hashCode = 0;
                }
                else
                {
                    _hashCode = _anchor.GetHashCode();
                }
            }
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Outopos
{
    [DataContract(Name = "ChatTopicContent", Namespace = "http://Library/Net/Outopos")]
    public sealed class ChatTopicContent : ItemBase<ChatTopicContent>
    {
        private enum SerializeId : byte
        {
            Comment = 0,
        }

        private string _comment;

        public static readonly int MaxCommentLength = 1024 * 32;

        public ChatTopicContent(string comment)
        {
            this.Comment = comment;
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
            if ((object)obj == null || !(obj is ChatTopicContent)) return false;

            return this.Equals((ChatTopicContent)obj);
        }

        public override bool Equals(ChatTopicContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.Comment != other.Comment)
            {
                return false;
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
                if (value != null && value.Length > ChatTopicContent.MaxCommentLength)
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
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "TopicContent", Namespace = "http://Library/Net/Lair")]
    public sealed class TopicContent : ItemBase<TopicContent>, ITopicContent
    {
        private enum SerializeId : byte
        {
            Content = 0,
            FormatType = 1,
        }

        private string _content = null;

        private ContentFormatType _formatType;

        public static readonly int MaxContentLength = 1024 * 32;

        public TopicContent(string content, ContentFormatType formatType)
        {
            this.Content = content;

            this.FormatType = formatType;
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
                    if (id == (byte)SerializeId.Content)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.Content = reader.ReadToEnd();
                        }
                    }

                    else if (id == (byte)SerializeId.FormatType)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.FormatType = (ContentFormatType)Enum.Parse(typeof(ContentFormatType), reader.ReadToEnd());
                        }
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // Content
            if (this.Content != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(this.Content);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Content);

                streams.Add(bufferStream);
            }

            // FormatType
            if (this.FormatType != 0)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(this.FormatType.ToString());
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.FormatType);

                streams.Add(bufferStream);
            }

            return new JoinStream(streams);
        }

        public override int GetHashCode()
        {
            if (_content == null) return 0;
            else return _content.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is TopicContent)) return false;

            return this.Equals((TopicContent)obj);
        }

        public override bool Equals(TopicContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Content != other.Content

                || this.FormatType != other.FormatType)
            {
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            return this.Content;
        }

        public override TopicContent DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return TopicContent.Import(stream, BufferManager.Instance);
            }
        }

        #region ITopicContent

        [DataMember(Name = "Content")]
        public string Content
        {
            get
            {
                return _content;
            }
            private set
            {
                if (value != null && value.Length > TopicContent.MaxContentLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _content = value;
                }
            }
        }

        #endregion

        #region IContentFormatType

        [DataMember(Name = "FormatType")]
        public ContentFormatType FormatType
        {
            get
            {
                return _formatType;
            }
            set
            {
                if (!Enum.IsDefined(typeof(ContentFormatType), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _formatType = value;
                }
            }
        }

        #endregion
    }
}

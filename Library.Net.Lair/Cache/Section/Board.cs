using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Library;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "Board", Namespace = "http://Library/Net/Lair")]
    public sealed class Board : ItemBase<Board>, IBoard<Channel>
    {
        private enum SerializeId : byte
        {
            Channel = 0,
            Content = 1,
        }

        private Channel _channel = null;
        private string _content = null;

        public const int MaxContentLength = 1024 * 8;

        public Board(Channel channel, string content, DigitalSignature digitalSignature)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            if (string.IsNullOrWhiteSpace(content)) throw new ArgumentNullException("content");

            this.Channel = channel;
            this.Content = content;
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
                    if (id == (byte)SerializeId.Channel)
                    {
                        this.Channel = Channel.Import(rangeStream, bufferManager);
                    }
                    else if (id == (byte)SerializeId.Content)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.Content = reader.ReadToEnd();
                        }
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // Channel
            if (this.Channel != null)
            {
                Stream exportStream = this.Channel.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Channel);

                streams.Add(new JoinStream(bufferStream, exportStream));
            }
            // Content
            if (this.Content != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                {
                    writer.Write(this.Content);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Content);

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
            if ((object)obj == null || !(obj is Board)) return false;

            return this.Equals((Board)obj);
        }

        public override bool Equals(Board other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Channel != other.Channel
                || this.Content != other.Content)
            {
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            return this.Content;
        }

        public override Board DeepClone()
        {
            using (var bufferManager = new BufferManager())
            using (var stream = this.Export(bufferManager))
            {
                return Board.Import(stream, bufferManager);
            }
        }

        #region IBoard<Channel>

        [DataMember(Name = "Channel")]
        public Channel Channel
        {
            get
            {
                return _channel;
            }
            private set
            {
                _channel = value;
            }
        }

        [DataMember(Name = "Content")]
        public string Content
        {
            get
            {
                return _content;
            }
            private set
            {
                if (value != null && value.Length > Board.MaxContentLength)
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
    }
}

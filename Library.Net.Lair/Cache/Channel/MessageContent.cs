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
            Text = 0,
            Anchor = 1,
        }

        private string _text = null;
        private KeyCollection _anchors = null;

        public static readonly int MaxTextLength = 1024 * 4;
        public static readonly int MaxAnchorsCount = 32;

        public MessageContent(string text, IEnumerable<Key> anchors)
        {
            this.Text = text;
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
                    if (id == (byte)SerializeId.Text)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.Text = reader.ReadToEnd();
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

            // Text
            if (this.Text != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(this.Text);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Text);

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
            if (_text == null) return 0;
            else return _text.GetHashCode();
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

            if (this.Text != other.Text
                || (this.Anchors == null) != (other.Anchors == null))
            {
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            return this.Text;
        }

        public override MessageContent DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return MessageContent.Import(stream, BufferManager.Instance);
            }
        }

        #region IMessageContent<Key>

        [DataMember(Name = "Text")]
        public string Text
        {
            get
            {
                return _text;
            }
            private set
            {
                if (value != null && value.Length > MessageContent.MaxTextLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _text = value;
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "MailContent", Namespace = "http://Library/Net/Lair")]
    public sealed class MailContent : ItemBase<MailContent>, IMailContent
    {
        private enum SerializeId : byte
        {
            Text = 0,
        }

        private string _text = null;

        public static readonly int MaxTextLength = 1024 * 4;

        public MailContent(string text)
        {
            this.Text = text;
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

            return new JoinStream(streams);
        }

        public override int GetHashCode()
        {
            if (_text == null) return 0;
            else return _text.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is MailContent)) return false;

            return this.Equals((MailContent)obj);
        }

        public override bool Equals(MailContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Text != other.Text)
            {
                return false;
            }

            return true;
        }

        public override MailContent DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return MailContent.Import(stream, BufferManager.Instance);
            }
        }

        #region IMailContent

        [DataMember(Name = "Text")]
        public string Text
        {
            get
            {
                return _text;
            }
            private set
            {
                if (value != null && value.Length > MailContent.MaxTextLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _text = value;
                }
            }
        }

        #endregion
    }
}

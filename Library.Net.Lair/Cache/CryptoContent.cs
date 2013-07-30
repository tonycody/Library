using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "CryptoContent", Namespace = "http://Library/Net/Lair")]
    public sealed class CryptoContent : ItemBase<CryptoContent>, ICryptoContent
    {
        private enum SerializeId : byte
        {
            Content = 0,

            CryptoAlgorithm = 1,
            CryptoKey = 2,
        }

        private ArraySegment<byte> _content;

        private CryptoAlgorithm _cryptoAlgorithm = 0;
        private byte[] _cryptoKey = null;

        private int _hashCode = 0;

        public static readonly int MaxCryptoKeyLength = 1024;

        public CryptoContent(ArraySegment<byte> content, CryptoAlgorithm cryptoAlgorithm, byte[] cryptoKey)
        {
            this.Content = content;

            this.CryptoAlgorithm = cryptoAlgorithm;
            this.CryptoKey = cryptoKey;
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
                        byte[] buff = bufferManager.TakeBuffer((int)rangeStream.Length);
                        rangeStream.Read(buff, 0, (int)rangeStream.Length);

                        this.Content = new ArraySegment<byte>(buff, 0, (int)rangeStream.Length);
                    }

                    else if (id == (byte)SerializeId.CryptoAlgorithm)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.CryptoAlgorithm = (CryptoAlgorithm)Enum.Parse(typeof(CryptoAlgorithm), reader.ReadToEnd());
                        }
                    }
                    else if (id == (byte)SerializeId.CryptoKey)
                    {
                        byte[] buffer = new byte[(int)rangeStream.Length];
                        rangeStream.Read(buffer, 0, buffer.Length);

                        this.CryptoKey = buffer;
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // Content
            if (this.Content.Array != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)this.Content.Count), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Content);
                bufferStream.Write(this.Content.Array, this.Content.Offset, this.Content.Count);

                streams.Add(bufferStream);
            }

            // CryptoAlgorithm
            if (this.CryptoAlgorithm != 0)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(this.CryptoAlgorithm.ToString());
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.CryptoAlgorithm);

                streams.Add(bufferStream);
            }
            // CryptoKey
            if (this.CryptoKey != null)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)this.CryptoKey.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.CryptoKey);
                bufferStream.Write(this.CryptoKey, 0, this.CryptoKey.Length);

                streams.Add(bufferStream);
            }

            return new JoinStream(streams);
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is CryptoContent)) return false;

            return this.Equals((CryptoContent)obj);
        }

        public override bool Equals(CryptoContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Content.Offset != other.Content.Offset
                || this.Content.Count != other.Content.Count
                || (this.Content.Array == null) != (other.Content.Array == null)

                || this.CryptoAlgorithm != other.CryptoAlgorithm
                || (this.CryptoKey == null) != (other.CryptoKey == null))
            {
                return false;
            }

            if (this.Content.Array != null && other.Content.Array != null)
            {
                if (!Collection.Equals(this.Content.Array, this.Content.Offset, other.Content.Array, other.Content.Offset, this.Content.Count)) return false;
            }

            if (this.CryptoKey != null && other.CryptoKey != null)
            {
                if (!Collection.Equals(this.CryptoKey, other.CryptoKey)) return false;
            }

            return true;
        }

        public override CryptoContent DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return CryptoContent.Import(stream, BufferManager.Instance);
            }
        }

        #region ICryptoContent

        [DataMember(Name = "Content")]
        public ArraySegment<byte> Content
        {
            get
            {
                return _content;
            }
            set
            {
                _content = value;
            }
        }

        #endregion

        #region ICryptoAlgorithm

        [DataMember(Name = "CryptoAlgorithm")]
        public CryptoAlgorithm CryptoAlgorithm
        {
            get
            {
                return _cryptoAlgorithm;
            }
            set
            {
                if (!Enum.IsDefined(typeof(CryptoAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _cryptoAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "CryptoKey")]
        public byte[] CryptoKey
        {
            get
            {
                return _cryptoKey;
            }
            set
            {
                if (value != null && value.Length > CryptoContent.MaxCryptoKeyLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _cryptoKey = value;
                }

                if (value != null && value.Length != 0)
                {
                    if (value.Length >= 4) _hashCode = BitConverter.ToInt32(value, 0) & 0x7FFFFFFF;
                    else if (value.Length >= 2) _hashCode = BitConverter.ToUInt16(value, 0);
                    else _hashCode = value[0];
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }

        #endregion
    }
}

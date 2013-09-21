using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "WhisperCryptoInformation", Namespace = "http://Library/Net/Lair")]
    public sealed class WhisperCryptoInformation : ItemBase<WhisperCryptoInformation>, IWhisperCryptoInformation
    {
        private enum SerializeId : byte
        {
            CryptoAlgorithm = 0,
            CryptoKey = 1,
        }

        private CryptoAlgorithm _cryptoAlgorithm = 0;
        private byte[] _cryptoKey = null;

        private int _hashCode = 0;

        public static readonly int MaxCryptoKeyLength = 1024;

        public WhisperCryptoInformation(CryptoAlgorithm cryptoAlgorithm)
        {
            var cryptoKey = new byte[32];
            (new RNGCryptoServiceProvider()).GetBytes(cryptoKey);

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
                    if (id == (byte)SerializeId.CryptoAlgorithm)
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
            if ((object)obj == null || !(obj is WhisperCryptoInformation)) return false;

            return this.Equals((WhisperCryptoInformation)obj);
        }

        public override bool Equals(WhisperCryptoInformation other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.CryptoAlgorithm != other.CryptoAlgorithm
                || (this.CryptoKey == null) != (other.CryptoKey == null))
            {
                return false;
            }

            if (this.CryptoKey != null && other.CryptoKey != null)
            {
                if (!Collection.Equals(this.CryptoKey, other.CryptoKey)) return false;
            }

            return true;
        }

        public override WhisperCryptoInformation DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return WhisperCryptoInformation.Import(stream, BufferManager.Instance);
            }
        }

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
                if (value != null && value.Length > WhisperCryptoInformation.MaxCryptoKeyLength)
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

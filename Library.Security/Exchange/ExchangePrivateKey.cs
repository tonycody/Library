using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Library.Io;

namespace Library.Security
{
    [DataContract(Name = "ExchangePrivateKey", Namespace = "http://Library/Security")]
    public sealed class ExchangePrivateKey : ItemBase<ExchangePrivateKey>, IExchangeDecrypt
    {
        private enum SerializeId : byte
        {
            ExchangeAlgorithm = 0,
            PrivateKey = 1,
        }

        private volatile ExchangeAlgorithm _exchangeAlgorithm = 0;
        private volatile byte[] _privateKey;

        private volatile int _hashCode;

        public static readonly int MaxPrivatekeyLength = 1024 * 8;

        public ExchangePrivateKey(Exchange exchange)
        {
            this.ExchangeAlgorithm = exchange.ExchangeAlgorithm;
            this.PrivateKey = exchange.PrivateKey;
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
                    if (id == (byte)SerializeId.ExchangeAlgorithm)
                    {
                        this.ExchangeAlgorithm = (ExchangeAlgorithm)Enum.Parse(typeof(ExchangeAlgorithm), ItemUtilities.GetString(rangeStream));
                    }
                    else if (id == (byte)SerializeId.PrivateKey)
                    {
                        this.PrivateKey = ItemUtilities.GetByteArray(rangeStream);
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            BufferStream bufferStream = new BufferStream(bufferManager);

            // ExchangeAlgorithm
            if (this.ExchangeAlgorithm != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.ExchangeAlgorithm, this.ExchangeAlgorithm.ToString());
            }
            // PrivateKey
            if (this.PrivateKey != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.PrivateKey, this.PrivateKey);
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is ExchangePrivateKey)) return false;

            return this.Equals((ExchangePrivateKey)obj);
        }

        public override bool Equals(ExchangePrivateKey other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.ExchangeAlgorithm != other.ExchangeAlgorithm
                || ((this.PrivateKey == null) != (other.PrivateKey == null)))
            {
                return false;
            }

            if (this.PrivateKey != null && other.PrivateKey != null)
            {
                if (!Unsafe.Equals(this.PrivateKey, other.PrivateKey)) return false;
            }

            return true;
        }

        [DataMember(Name = "ExchangeAlgorithm")]
        public ExchangeAlgorithm ExchangeAlgorithm
        {
            get
            {
                return _exchangeAlgorithm;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(ExchangeAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _exchangeAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "PrivateKey")]
        public byte[] PrivateKey
        {
            get
            {
                return _privateKey;
            }
            private set
            {
                if (value != null && (value.Length > Exchange.MaxPublickeyLength))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _privateKey = value;
                }

                if (value != null)
                {
                    _hashCode = ItemUtilities.GetHashCode(value);
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }
    }
}

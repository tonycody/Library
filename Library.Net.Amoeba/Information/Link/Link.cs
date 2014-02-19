using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Link", Namespace = "http://Library/Net/Amoeba")]
    public sealed class Link : ItemBase<Link>, ILink, ICloneable<Link>, IThisLock
    {
        private enum SerializeId : byte
        {
            TrustSignature = 0,
        }

        private SignatureCollection _trustSignatures;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxTrustSignatureCount = 1024;

        public Link()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
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
                        if (id == (byte)SerializeId.TrustSignature)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.TrustSignatures.Add(reader.ReadToEnd());
                            }
                        }
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                Encoding encoding = new UTF8Encoding(false);

                // TrustSignatures
                foreach (var value in this.TrustSignatures)
                {
                    byte[] buffer = null;

                    try
                    {
                        buffer = bufferManager.TakeBuffer(encoding.GetMaxByteCount(value.Length));
                        var length = encoding.GetBytes(value, 0, value.Length, buffer, 0);

                        bufferStream.Write(NetworkConverter.GetBytes(length), 0, 4);
                        bufferStream.WriteByte((byte)SerializeId.TrustSignature);
                        bufferStream.Write(buffer, 0, length);
                    }
                    finally
                    {
                        if (buffer != null)
                        {
                            bufferManager.ReturnBuffer(buffer);
                        }
                    }
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.TrustSignatures.Count == 0) return 0;
                else return this.TrustSignatures[0].GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Link)) return false;

            return this.Equals((Link)obj);
        }

        public override bool Equals(Link other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (!Collection.Equals(this.TrustSignatures, other.TrustSignatures))
            {
                return false;
            }

            return true;
        }

        #region ILink

        [DataMember(Name = "TrustSignatures")]
        public SignatureCollection TrustSignatures
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_trustSignatures == null)
                        _trustSignatures = new SignatureCollection(Link.MaxTrustSignatureCount);

                    return _trustSignatures;
                }
            }
        }

        #endregion

        #region ICloneable<Link>

        public Link Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Link.Import(stream, BufferManager.Instance);
                }
            }
        }

        #endregion

        #region IThisLock

        public object ThisLock
        {
            get
            {
                if (_thisLock == null)
                {
                    lock (_initializeLock)
                    {
                        if (_thisLock == null)
                        {
                            _thisLock = new object();
                        }
                    }
                }

                return _thisLock;
            }
        }

        #endregion
    }
}

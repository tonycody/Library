using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Connections.SecureVersion1
{
    [DataContract(Name = "ConnectionSignature", Namespace = "http://Library/Net/Connection/SecureVersion1")]
    sealed class ConnectionSignature : CertificateItemBase<ConnectionSignature>, IThisLock
    {
        private enum SerializeId : byte
        {
            Key = 0,
            CreationTime = 1,

            Certificate = 2,
        }

        private byte[] _key;
        private DateTime _creationTime;

        private Certificate _certificate;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxKeyLength = 8192;

        public ConnectionSignature()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
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
                        if (id == (byte)SerializeId.Key)
                        {
                            byte[] buffer = new byte[rangeStream.Length];
                            rangeStream.Read(buffer, 0, buffer.Length);

                            this.Key = buffer;
                        }
                        else if (id == (byte)SerializeId.CreationTime)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.CreationTime = DateTime.ParseExact(reader.ReadToEnd(), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                            }
                        }

                        else if (id == (byte)SerializeId.Certificate)
                        {
                            this.Certificate = Certificate.Import(rangeStream, bufferManager);
                        }
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            lock (this.ThisLock)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Key
                if (this.Key != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.Key.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Key);
                    bufferStream.Write(this.Key, 0, this.Key.Length);

                    streams.Add(bufferStream);
                }
                // CreationTime
                if (this.CreationTime != DateTime.MinValue)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo));
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.CreationTime);

                    streams.Add(bufferStream);
                }

                // Certificate
                if (this.Certificate != null)
                {
                    Stream exportStream = this.Certificate.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Certificate);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }

                return new JoinStream(streams);
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                return this.CreationTime.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is ConnectionSignature)) return false;

            return this.Equals((ConnectionSignature)obj);
        }

        public override bool Equals(ConnectionSignature other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if ((this.Key == null) != (other.Key == null)
                || this.CreationTime != other.CreationTime

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            if (this.Key != null && other.Key != null)
            {
                if (!Collection.Equals(this.Key, other.Key)) return false;
            }

            return true;
        }

        public override ConnectionSignature DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return ConnectionSignature.Import(stream, BufferManager.Instance);
                }
            }
        }

        public override void CreateCertificate(DigitalSignature digitalSignature)
        {
            lock (this.ThisLock)
            {
                base.CreateCertificate(digitalSignature);
            }
        }

        public override bool VerifyCertificate()
        {
            lock (this.ThisLock)
            {
                return base.VerifyCertificate();
            }
        }

        protected override Stream GetCertificateStream()
        {
            lock (this.ThisLock)
            {
                var temp = this.Certificate;
                this.Certificate = null;

                try
                {
                    return this.Export(BufferManager.Instance);
                }
                finally
                {
                    this.Certificate = temp;
                }
            }
        }

        public override Certificate Certificate
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _certificate;
                }
            }
            protected set
            {
                lock (this.ThisLock)
                {
                    _certificate = value;
                }
            }
        }

        [DataMember(Name = "Key")]
        public byte[] Key
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _key;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > ConnectionSignature.MaxKeyLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _key = value;
                    }
                }
            }
        }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _creationTime;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    var temp = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo);
                    _creationTime = DateTime.ParseExact(temp, "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                }
            }
        }

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

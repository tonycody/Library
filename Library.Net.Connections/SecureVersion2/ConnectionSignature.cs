using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Connections.SecureVersion2
{
    [DataContract(Name = "ConnectionSignature", Namespace = "http://Library/Net/Connection/SecureVersion2")]
    sealed class ConnectionSignature : CertificateItemBase<ConnectionSignature>, IThisLock
    {
        private enum SerializeId : byte
        {
            CreationTime = 0,
            Key = 1,
            MyHash = 2,
            OtherHash = 3,

            Certificate = 4,
        }

        private DateTime _creationTime = DateTime.MinValue;
        private byte[] _key = null;
        private byte[] _myHash = null;
        private byte[] _otherHash = null;

        private Certificate _certificate;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        public static readonly int MaxKeyLength = 8192;
        public static readonly int MaxMyHashLength = 64;
        public static readonly int MaxOtherHashLength = 64;

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
                        if (id == (byte)SerializeId.CreationTime)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.CreationTime = DateTime.ParseExact(reader.ReadToEnd(), "yyyy-MM-ddTHH:mm:ssZ", System.Globalization.DateTimeFormatInfo.InvariantInfo).ToUniversalTime();
                            }
                        }
                        if (id == (byte)SerializeId.Key)
                        {
                            byte[] buffer = new byte[rangeStream.Length];
                            rangeStream.Read(buffer, 0, buffer.Length);

                            this.Key = buffer;
                        }
                        if (id == (byte)SerializeId.MyHash)
                        {
                            byte[] buffer = new byte[rangeStream.Length];
                            rangeStream.Read(buffer, 0, buffer.Length);

                            this.MyHash = buffer;
                        }
                        if (id == (byte)SerializeId.OtherHash)
                        {
                            byte[] buffer = new byte[rangeStream.Length];
                            rangeStream.Read(buffer, 0, buffer.Length);

                            this.OtherHash = buffer;
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
                // Key
                if (this.Key != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.Key.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Key);
                    bufferStream.Write(this.Key, 0, this.Key.Length);

                    streams.Add(bufferStream);
                }
                // MyHash
                if (this.MyHash != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.MyHash.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.MyHash);
                    bufferStream.Write(this.MyHash, 0, this.MyHash.Length);

                    streams.Add(bufferStream);
                }
                // OtherHash
                if (this.OtherHash != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.OtherHash.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.OtherHash);
                    bufferStream.Write(this.OtherHash, 0, this.OtherHash.Length);

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

            if (this.CreationTime != other.CreationTime
                || (this.Key == null) != (other.Key == null)
                || (this.MyHash == null) != (other.MyHash == null)
                || (this.OtherHash == null) != (other.OtherHash == null)

                || this.Certificate != other.Certificate)
            {
                return false;
            }

            if (this.Key != null && other.Key != null)
            {
                if (!Collection.Equals(this.Key, other.Key)) return false;
            }

            if (this.MyHash != null && other.MyHash != null)
            {
                if (!Collection.Equals(this.MyHash, other.MyHash)) return false;
            }

            if (this.OtherHash != null && other.OtherHash != null)
            {
                if (!Collection.Equals(this.OtherHash, other.OtherHash)) return false;
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

        [DataMember(Name = "MyHash")]
        public byte[] MyHash
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _myHash;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > ConnectionSignature.MaxMyHashLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _myHash = value;
                    }
                }
            }
        }

        [DataMember(Name = "OtherHash")]
        public byte[] OtherHash
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _otherHash;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > ConnectionSignature.MaxOtherHashLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _otherHash = value;
                    }
                }
            }
        }

        #region IThisLock

        public object ThisLock
        {
            get
            {
                lock (_thisStaticLock)
                {
                    if (_thisLock == null)
                        _thisLock = new object();

                    return _thisLock;
                }
            }
        }

        #endregion
    }
}

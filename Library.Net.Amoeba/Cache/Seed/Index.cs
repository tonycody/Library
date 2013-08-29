using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Index", Namespace = "http://Library/Net/Amoeba")]
    public sealed class Index : ItemBase<Index>, IIndex<Group, Key>, IThisLock
    {
        private enum SerializeId : byte
        {
            Group = 0,

            CompressionAlgorithm = 1,

            CryptoAlgorithm = 2,
            CryptoKey = 3,
        }

        private GroupCollection _groups = null;

        private CompressionAlgorithm _compressionAlgorithm = 0;

        private CryptoAlgorithm _cryptoAlgorithm = 0;
        private byte[] _cryptoKey = null;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        public static readonly int MaxCryptoKeyLength = 64;

        public Index()
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
                        if (id == (byte)SerializeId.Group)
                        {
                            this.Groups.Add(Group.Import(rangeStream, bufferManager));
                        }

                        else if (id == (byte)SerializeId.CompressionAlgorithm)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.CompressionAlgorithm = (CompressionAlgorithm)Enum.Parse(typeof(CompressionAlgorithm), reader.ReadToEnd());
                            }
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
        }

        public override Stream Export(BufferManager bufferManager)
        {
            lock (this.ThisLock)
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Groups
                foreach (var g in this.Groups)
                {
                    Stream exportStream = g.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Group);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }

                // CompressionAlgorithm
                if (this.CompressionAlgorithm != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.CompressionAlgorithm);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.CompressionAlgorithm);

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
                        writer.Write(this.CryptoAlgorithm);
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
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.Groups.Count == 0) return 0;
                else if (this.Groups[0].Keys.Count == 0) return 0;
                else return this.Groups[0].Keys[0].GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Index)) return false;

            return this.Equals((Index)obj);
        }

        public override bool Equals(Index other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if ((this.Groups == null) != (other.Groups == null)

                || this.CompressionAlgorithm != other.CompressionAlgorithm

                || this.CryptoAlgorithm != other.CryptoAlgorithm
                || (this.CryptoKey == null) != (other.CryptoKey == null))
            {
                return false;
            }

            if (this.Groups != null && other.Groups != null)
            {
                if (!Collection.Equals(this.Groups, other.Groups)) return false;
            }

            if (this.CryptoKey != null && other.CryptoKey != null)
            {
                if (!Collection.Equals(this.CryptoKey, other.CryptoKey)) return false;
            }

            return true;
        }

        public override Index DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Index.Import(stream, BufferManager.Instance);
                }
            }
        }

        #region IIndex<Group, Header>

        IList<Group> IIndex<Group, Key>.Groups
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.Groups;
                }
            }
        }

        [DataMember(Name = "Groups")]
        public GroupCollection Groups
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_groups == null)
                        _groups = new GroupCollection();

                    return _groups;
                }
            }
        }

        #endregion

        #region ICompressionAlgorithm

        [DataMember(Name = "CompressionAlgorithm")]
        public CompressionAlgorithm CompressionAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _compressionAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (!Enum.IsDefined(typeof(CompressionAlgorithm), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _compressionAlgorithm = value;
                    }
                }
            }
        }

        #endregion

        #region ICryptoAlgorithm

        [DataMember(Name = "CryptoAlgorithm")]
        public CryptoAlgorithm CryptoAlgorithm
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _cryptoAlgorithm;
                }
            }
            set
            {
                lock (this.ThisLock)
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
        }

        [DataMember(Name = "CryptoKey")]
        public byte[] CryptoKey
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _cryptoKey;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Index.MaxCryptoKeyLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _cryptoKey = value;
                    }
                }
            }
        }

        #endregion

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

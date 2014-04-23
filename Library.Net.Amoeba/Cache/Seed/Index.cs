using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Index", Namespace = "http://Library/Net/Amoeba")]
    public sealed class Index : ItemBase<Index>, IIndex<Group, Key>, ICloneable<Index>, IThisLock
    {
        private enum SerializeId : byte
        {
            Group = 0,

            CompressionAlgorithm = 1,

            CryptoAlgorithm = 2,
            CryptoKey = 3,
        }

        private GroupCollection _groups;

        private CompressionAlgorithm _compressionAlgorithm = 0;

        private CryptoAlgorithm _cryptoAlgorithm = 0;
        private byte[] _cryptoKey;

        private volatile object _thisLock;

        public static readonly int MaxCryptoKeyLength = 64;

        public Index()
        {

        }

        protected override void Initialize()
        {
            _thisLock = new object();
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            lock (this.ThisLock)
            {
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
                            this.CompressionAlgorithm = (CompressionAlgorithm)Enum.Parse(typeof(CompressionAlgorithm), ItemUtilities.GetString(rangeStream));
                        }

                        else if (id == (byte)SerializeId.CryptoAlgorithm)
                        {
                            this.CryptoAlgorithm = (CryptoAlgorithm)Enum.Parse(typeof(CryptoAlgorithm), ItemUtilities.GetString(rangeStream));
                        }
                        else if (id == (byte)SerializeId.CryptoKey)
                        {
                            this.CryptoKey = ItemUtilities.GetByteArray(rangeStream);
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

                // Groups
                foreach (var value in this.Groups)
                {
                    using (var stream = value.Export(bufferManager))
                    {
                        ItemUtilities.Write(bufferStream, (byte)SerializeId.Group, stream);
                    }
                }

                // CompressionAlgorithm
                if (this.CompressionAlgorithm != 0)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.CompressionAlgorithm, this.CompressionAlgorithm.ToString());
                }

                // CryptoAlgorithm
                if (this.CryptoAlgorithm != 0)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.CryptoAlgorithm, this.CryptoAlgorithm.ToString());
                }
                // CryptoKey
                if (this.CryptoKey != null)
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.CryptoKey, this.CryptoKey);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                return bufferStream;
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

            if (!CollectionUtilities.Equals(this.Groups, other.Groups)

                || this.CompressionAlgorithm != other.CompressionAlgorithm

                || this.CryptoAlgorithm != other.CryptoAlgorithm
                || (this.CryptoKey == null) != (other.CryptoKey == null))
            {
                return false;
            }

            if (this.CryptoKey != null && other.CryptoKey != null)
            {
                if (!Native.Equals(this.CryptoKey, other.CryptoKey)) return false;
            }

            return true;
        }

        #region IIndex<Group, Header>

        ICollection<Group> IIndex<Group, Key>.Groups
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

        #region ICloneable<Index>

        public Index Clone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Index.Import(stream, BufferManager.Instance);
                }
            }
        }

        #endregion

        #region IThisLock

        public object ThisLock
        {
            get
            {
                return _thisLock;
            }
        }

        #endregion
    }
}

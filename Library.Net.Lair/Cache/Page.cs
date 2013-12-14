using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Lair
{
    [DataContract(Name = "Page", Namespace = "http://Library/Net/Lair")]
    public sealed class Page : ItemBase<Page>, IPage
    {
        private enum SerializeId : byte
        {
            Name = 0,
            FormatType = 1,
            Hypertext = 2,
        }

        private string _name;
        private HypertextFormatType _formatType;
        private string _hypertext;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxNameLength = 1024;
        public static readonly int MaxHypertextLength = 1024 * 32;

        public Page(string name, HypertextFormatType formatType, string hypertext)
        {
            this.Name = name;
            this.FormatType = formatType;
            this.Hypertext = hypertext;
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
                        if (id == (byte)SerializeId.Name)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Name = reader.ReadToEnd();
                            }
                        }
                        else if (id == (byte)SerializeId.FormatType)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.FormatType = (HypertextFormatType)Enum.Parse(typeof(HypertextFormatType), reader.ReadToEnd());
                            }
                        }
                        else if (id == (byte)SerializeId.Hypertext)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Hypertext = reader.ReadToEnd();
                            }
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

                // Name
                if (this.Name != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.Name);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Name);

                    streams.Add(bufferStream);
                }
                // FormatType
                if (this.FormatType != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.FormatType.ToString());
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.FormatType);

                    streams.Add(bufferStream);
                }
                // Hypertext
                if (this.Hypertext != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                    using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                    {
                        writer.Write(this.Hypertext);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Hypertext);

                    streams.Add(bufferStream);
                }

                return new JoinStream(streams);
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.Hypertext == null) return 0;
                else return this.Hypertext.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Page)) return false;

            return this.Equals((Page)obj);
        }

        public override bool Equals(Page other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if (this.Name != other.Name
                || this.FormatType != other.FormatType
                || this.Hypertext != other.Hypertext)
            {
                return false;
            }

            return true;
        }

        private object ThisLock
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

        #region IArchivePage

        [DataMember(Name = "Name")]
        public string Name
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _name;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Page.MaxNameLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _name = value;
                    }
                }
            }
        }

        [DataMember(Name = "FormatType")]
        public HypertextFormatType FormatType
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _formatType;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (!Enum.IsDefined(typeof(HypertextFormatType), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _formatType = value;
                    }
                }
            }
        }

        [DataMember(Name = "Hypertext")]
        public string Hypertext
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _hypertext;
                }
            }
            private set
            {
                lock (this.ThisLock)
                {
                    if (value != null && value.Length > Page.MaxHypertextLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _hypertext = value;
                    }
                }
            }
        }

        #endregion
        
        #region IComputeHash

        private byte[] _sha512_hash;

        public byte[] GetHash(HashAlgorithm hashAlgorithm)
        {
            lock (this.ThisLock)
            {
                if (_sha512_hash == null)
                {
                    using (var stream = this.Export(BufferManager.Instance))
                    {
                        _sha512_hash = Sha512.ComputeHash(stream);
                    }
                }

                if (hashAlgorithm == HashAlgorithm.Sha512)
                {
                    return _sha512_hash;
                }

                return null;
            }
        }

        public bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm)
        {
            lock (this.ThisLock)
            {
                return Collection.Equals(this.GetHash(hashAlgorithm), hash);
            }
        }

        #endregion
    }
}

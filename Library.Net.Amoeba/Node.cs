using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Amoeba
{
    /// <summary>
    /// ノードに関する情報を表します
    /// </summary>
    [DataContract(Name = "Node", Namespace = "http://Library/Net/Amoeba")]
    public sealed class Node : ItemBase<Node>, INode, IThisLock
    {
        private enum SerializeId : byte
        {
            Id = 0,
            Uri = 1,
        }

        private byte[] _id;
        private UriCollection _uris;

        private int _hashCode = 0;

        private object _thisLock;
        private static object _thisStaticLock = new object();

        public static readonly int MaxIdLength = 64;
        public static readonly int MaxUrisCount = 32;

        public Node()
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
                    int id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Id)
                        {
                            byte[] buffer = new byte[rangeStream.Length];
                            rangeStream.Read(buffer, 0, buffer.Length);

                            this.Id = buffer;
                        }

                        else if (id == (byte)SerializeId.Uri)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Uris.Add(reader.ReadToEnd());
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

                // Id
                if (this.Id != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)this.Id.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Id);
                    bufferStream.Write(this.Id, 0, this.Id.Length);

                    streams.Add(bufferStream);
                }

                // Uris
                foreach (var u in this.Uris)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                    using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                    {
                        writer.Write(u);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Uri);

                    streams.Add(bufferStream);
                }

                return new JoinStream(streams);
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                return _hashCode;
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Node)) return false;

            return this.Equals((Node)obj);
        }

        public override bool Equals(Node other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if ((this.Id == null) != (other.Id == null)
                || (this.Uris == null) != (other.Uris == null))
            {
                return false;
            }

            if (this.Id != null && other.Id != null)
            {
                if (this.Id.Length != other.Id.Length) return false;

                for (int i = 0; i < this.Id.Length; i++) if (this.Id[i] != other.Id[i]) return false;
            }

            if (this.Uris != null && other.Uris != null)
            {
                if (this.Uris.Count != other.Uris.Count) return false;

                for (int i = 0; i < this.Uris.Count; i++) if (this.Uris[i] != other.Uris[i]) return false;
            }

            return true;
        }

        public override string ToString()
        {
            lock (this.ThisLock)
            {
                return String.Join(", ", this.Uris);
            }
        }

        public override Node DeepClone()
        {
            lock (this.ThisLock)
            {
                using (var stream = this.Export(BufferManager.Instance))
                {
                    return Node.Import(stream, BufferManager.Instance);
                }
            }
        }

        #region INode

        /// <summary>
        /// Idを取得または設定します
        /// </summary>
        [DataMember(Name = "Id")]
        public byte[] Id
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _id;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    if (value != null && (value.Length > Node.MaxIdLength))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _id = value;
                    }

                    if (value != null && value.Length != 0)
                    {
                        try
                        {
                            if (value.Length >= 4) _hashCode = Math.Abs(BitConverter.ToInt32(value, 0));
                            else if (value.Length >= 2) _hashCode = BitConverter.ToUInt16(value, 0);
                            else _hashCode = value[0];
                        }
                        catch
                        {
                            _hashCode = 0;
                        }
                    }
                    else
                    {
                        _hashCode = 0;
                    }
                }
            }
        }

        #endregion

        [DataMember(Name = "Uris")]
        public UriCollection Uris
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_uris == null)
                        _uris = new UriCollection(Node.MaxUrisCount);

                    return _uris;
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

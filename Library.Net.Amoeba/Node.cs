using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public sealed class Node : ItemBase<Node>, INode
    {
        private enum SerializeId : byte
        {
            Id = 0,
            Uri = 1,
        }

        private volatile byte[] _id;
        private volatile UriCollection _uris;

        private volatile int _hashCode;

        public static readonly int MaxIdLength = 64;
        public static readonly int MaxUriCount = 32;

        public Node(byte[] id, IEnumerable<string> uris)
        {
            this.Id = id;
            if (uris != null) this.ProtectedUris.AddRange(uris);
        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
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
                        this.Id = ItemUtility.GetByteArray(rangeStream);
                    }

                    else if (id == (byte)SerializeId.Uri)
                    {
                        this.ProtectedUris.Add(ItemUtility.GetString(rangeStream));
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            BufferStream bufferStream = new BufferStream(bufferManager);

            // Id
            if (this.Id != null)
            {
                ItemUtility.Write(bufferStream, (byte)SerializeId.Id, this.Id);
            }

            // Uris
            foreach (var value in this.Uris)
            {
                ItemUtility.Write(bufferStream, (byte)SerializeId.Uri, value);
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
            if ((object)obj == null || !(obj is Node)) return false;

            return this.Equals((Node)obj);
        }

        public override bool Equals(Node other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if ((this.Id == null) != (other.Id == null)
                || (this.Uris == null) != (other.Uris == null))
            {
                return false;
            }

            if (this.Id != null && other.Id != null)
            {
                if (!Collection.Equals(this.Id, other.Id)) return false;
            }

            if (this.Uris != null && other.Uris != null)
            {
                if (!Collection.Equals(this.Uris, other.Uris)) return false;
            }

            return true;
        }

        public override string ToString()
        {
            return String.Join(", ", this.Uris);
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
                return _id;
            }
            private set
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

        public IEnumerable<string> Uris
        {
            get
            {
                foreach (var item in this.ProtectedUris)
                {
                    yield return item;
                }
            }
        }

        [DataMember(Name = "Uris")]
        private UriCollection ProtectedUris
        {
            get
            {
                if (_uris == null)
                    _uris = new UriCollection(Node.MaxUriCount);

                return _uris;
            }
        }
    }
}

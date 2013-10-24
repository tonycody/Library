using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "DocumentOpinion", Namespace = "http://Library/Net/Lair")]
    public sealed class DocumentOpinion : ItemBase<DocumentOpinion>, IDocumentOpinion<Key>
    {
        private enum SerializeId : byte
        {
            Good = 0,
            Bad = 1,
        }

        private KeyCollection _goods = null;
        private KeyCollection _bads = null;

        public static readonly int MaxGoodsCount = 1024;
        public static readonly int MaxBadsCount = 1024;

        public DocumentOpinion(IEnumerable<Key> goods, IEnumerable<Key> bads)
        {
            if (goods != null) this.ProtectedGoods.AddRange(goods);
            if (bads != null) this.ProtectedBads.AddRange(bads);
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
                    if (id == (byte)SerializeId.Good)
                    {
                        this.ProtectedGoods.Add(Key.Import(rangeStream, bufferManager));
                    }
                    else if (id == (byte)SerializeId.Bad)
                    {
                        this.ProtectedBads.Add(Key.Import(rangeStream, bufferManager));
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // Goods
            foreach (var a in this.Goods)
            {
                Stream exportStream = a.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Good);

                streams.Add(new JoinStream(bufferStream, exportStream));
            }
            // Bads
            foreach (var a in this.Bads)
            {
                Stream exportStream = a.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Bad);

                streams.Add(new JoinStream(bufferStream, exportStream));
            }

            return new JoinStream(streams);
        }

        public override int GetHashCode()
        {
            if (this.ProtectedGoods.Count == 0) return 0;
            else return this.ProtectedGoods[0].GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is DocumentOpinion)) return false;

            return this.Equals((DocumentOpinion)obj);
        }

        public override bool Equals(DocumentOpinion other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if ((this.Goods == null) != (other.Goods == null)
                || (this.Bads == null) != (other.Bads == null))
            {
                return false;
            }

            if (this.Goods != null && other.Goods != null)
            {
                if (!Collection.Equals(this.Goods, other.Goods)) return false;
            }

            if (this.Bads != null && other.Bads != null)
            {
                if (!Collection.Equals(this.Bads, other.Bads)) return false;
            }

            return true;
        }

        public override DocumentOpinion DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return DocumentOpinion.Import(stream, BufferManager.Instance);
            }
        }

        #region IDocumentOpinionsContent<Key>

        public IEnumerable<Key> Goods
        {
            get
            {
                return this.ProtectedGoods;
            }
        }

        [DataMember(Name = "Goods")]
        private KeyCollection ProtectedGoods
        {
            get
            {
                if (_goods == null)
                    _goods = new KeyCollection(DocumentOpinion.MaxGoodsCount);

                return _goods;
            }
        }

        public IEnumerable<Key> Bads
        {
            get
            {
                return this.ProtectedBads;
            }
        }

        [DataMember(Name = "Bads")]
        private KeyCollection ProtectedBads
        {
            get
            {
                if (_bads == null)
                    _bads = new KeyCollection(DocumentOpinion.MaxBadsCount);

                return _bads;
            }
        }

        #endregion
    }
}

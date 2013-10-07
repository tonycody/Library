using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "DocumentOpinionContent", Namespace = "http://Library/Net/Lair")]
    public sealed class DocumentOpinionContent : ItemBase<DocumentOpinionContent>, IDocumentOpinionContent
    {
        private enum SerializeId : byte
        {
            Good = 0,
            Bad = 1,
        }

        private SignatureCollection _goods = null;
        private SignatureCollection _bads = null;

        public static readonly int MaxGoodCount = 1024;
        public static readonly int MaxBadCount = 1024;

        public DocumentOpinionContent(IEnumerable<string> goods, IEnumerable<string> bads)
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
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.ProtectedGoods.Add(reader.ReadToEnd());
                        }
                    }
                    else if (id == (byte)SerializeId.Bad)
                    {
                        using (StreamReader reader = new StreamReader(rangeStream, encoding))
                        {
                            this.ProtectedBads.Add(reader.ReadToEnd());
                        }
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // Goods
            foreach (var g in this.Goods)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(g);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Good);

                streams.Add(bufferStream);
            }
            // Bads
            foreach (var b in this.Bads)
            {
                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.SetLength(5);
                bufferStream.Seek(5, SeekOrigin.Begin);

                using (WrapperStream wrapperStream = new WrapperStream(bufferStream, true))
                using (StreamWriter writer = new StreamWriter(wrapperStream, encoding))
                {
                    writer.Write(b);
                }

                bufferStream.Seek(0, SeekOrigin.Begin);
                bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Bad);

                streams.Add(bufferStream);
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
            if ((object)obj == null || !(obj is DocumentOpinionContent)) return false;

            return this.Equals((DocumentOpinionContent)obj);
        }

        public override bool Equals(DocumentOpinionContent other)
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

        public override DocumentOpinionContent DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return DocumentOpinionContent.Import(stream, BufferManager.Instance);
            }
        }

        #region IDocumentOpinionsContent

        public IEnumerable<string> Goods
        {
            get
            {
                return this.ProtectedGoods;
            }
        }

        [DataMember(Name = "Goods")]
        private SignatureCollection ProtectedGoods
        {
            get
            {
                if (_goods == null)
                    _goods = new SignatureCollection();

                return _goods;
            }
        }

        public IEnumerable<string> Bads
        {
            get
            {
                return this.ProtectedBads;
            }
        }

        [DataMember(Name = "Bads")]
        private SignatureCollection ProtectedBads
        {
            get
            {
                if (_bads == null)
                    _bads = new SignatureCollection();

                return _bads;
            }
        }

        #endregion
    }
}

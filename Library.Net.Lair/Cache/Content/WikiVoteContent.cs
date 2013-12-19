using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Lair
{
    [DataContract(Name = "WikiVoteContent", Namespace = "http://Library/Net/Lair")]
    sealed class WikiVoteContent : ItemBase<WikiVoteContent>, IWikiVoteContent<Anchor>
    {
        private enum SerializeId : byte
        {
            Good = 0,
            Bad = 1,
        }

        private AnchorCollection _goods;
        private AnchorCollection _bads;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxGoodCount = 1024;
        public static readonly int MaxBadCount = 1024;

        public WikiVoteContent(IEnumerable<Anchor> goods, IEnumerable<Anchor> bads)
        {
            if (goods != null) this.ProtectedGoods.AddRange(goods);
            if (bads != null) this.ProtectedBads.AddRange(bads);
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
                        if (id == (byte)SerializeId.Good)
                        {
                            this.ProtectedGoods.Add(Anchor.Import(rangeStream, bufferManager));
                        }
                        else if (id == (byte)SerializeId.Bad)
                        {
                            this.ProtectedBads.Add(Anchor.Import(rangeStream, bufferManager));
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
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.ProtectedGoods.Count == 0) return 0;
                else return this.ProtectedGoods[0].GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is WikiVoteContent)) return false;

            return this.Equals((WikiVoteContent)obj);
        }

        public override bool Equals(WikiVoteContent other)
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

        #region IWikiVotesContent<Anchor>

        private volatile ReadOnlyCollection<Anchor> _readOnlyGoods;

        public IEnumerable<Anchor> Goods
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyGoods == null)
                        _readOnlyGoods = new ReadOnlyCollection<Anchor>(this.ProtectedGoods);

                    return _readOnlyGoods;
                }
            }
        }

        [DataMember(Name = "Goods")]
        private AnchorCollection ProtectedGoods
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_goods == null)
                        _goods = new AnchorCollection(WikiVoteContent.MaxGoodCount);

                    return _goods;
                }
            }
        }

        private volatile ReadOnlyCollection<Anchor> _readOnlyBads;

        public IEnumerable<Anchor> Bads
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyBads == null)
                        _readOnlyBads = new ReadOnlyCollection<Anchor>(this.ProtectedBads);

                    return _readOnlyBads;
                }
            }
        }

        [DataMember(Name = "Bads")]
        private AnchorCollection ProtectedBads
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_bads == null)
                        _bads = new AnchorCollection(WikiVoteContent.MaxBadCount);

                    return _bads;
                }
            }
        }

        #endregion
    }
}

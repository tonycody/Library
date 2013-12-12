using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using System.Collections.ObjectModel;

namespace Library.Net.Lair
{
    [DataContract(Name = "ArchiveDocumentContent", Namespace = "http://Library/Net/Lair")]
    public sealed class ArchiveDocumentContent : ItemBase<ArchiveDocumentContent>, IArchiveDocumentContent<Page>
    {
        private enum SerializeId : byte
        {
            Page = 0,
        }

        private PageCollection _pages;

        private volatile object _thisLock;
        private static readonly object _initializeLock = new object();

        public static readonly int MaxPagesCount = 256;

        public ArchiveDocumentContent(IEnumerable<Page> pages)
        {
            if (pages != null) this.ProtectedPages.AddRange(pages);
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
                        if (id == (byte)SerializeId.Page)
                        {
                            this.ProtectedPages.Add(Page.Import(rangeStream, bufferManager));
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

                // Pages
                foreach (var p in this.Pages)
                {
                    Stream exportStream = p.Export(bufferManager);

                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Page);

                    streams.Add(new JoinStream(bufferStream, exportStream));
                }

                return new JoinStream(streams);
            }
        }

        public override int GetHashCode()
        {
            lock (this.ThisLock)
            {
                if (this.ProtectedPages.Count == 0) return 0;
                else return this.ProtectedPages[0].GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is ArchiveDocumentContent)) return false;

            return this.Equals((ArchiveDocumentContent)obj);
        }

        public override bool Equals(ArchiveDocumentContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if ((this.Pages == null) != (other.Pages == null))
            {
                return false;
            }

            if (this.Pages != null && other.Pages != null)
            {
                if (!Collection.Equals(this.Pages, other.Pages)) return false;
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

        #region IPage

        private volatile ReadOnlyCollection<Page> _readOnlyPages;

        public IEnumerable<Page> Pages
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_readOnlyPages == null)
                        _readOnlyPages = new ReadOnlyCollection<Page>(this.ProtectedPages);

                    return _readOnlyPages;
                }
            }
        }

        [DataMember(Name = "Pages")]
        private PageCollection ProtectedPages
        {
            get
            {
                lock (this.ThisLock)
                {
                    if (_pages == null)
                        _pages = new PageCollection(ArchiveDocumentContent.MaxPagesCount);

                    return _pages;
                }
            }
        }

        #endregion
    }
}

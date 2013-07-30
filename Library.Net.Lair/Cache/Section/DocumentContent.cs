using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "DocumentContent", Namespace = "http://Library/Net/Lair")]
    public sealed class DocumentContent : ItemBase<DocumentContent>, IDocumentContent<Page>
    {
        private enum SerializeId : byte
        {
            Page = 0,
        }

        private PageCollection _pages = null;

        private Certificate _certificate;

        public static readonly int MaxPagesCount = 1024;

        public DocumentContent(IEnumerable<Page> pages)
        {
            if (pages != null) this.ProtectedPages.AddRange(pages);
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
                    if (id == (byte)SerializeId.Page)
                    {
                        this.ProtectedPages.Add(Page.Import(rangeStream, bufferManager));
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // Pages
            foreach (var p in this.ProtectedPages)
            {
                Stream exportStream = p.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.Page);

                streams.Add(new JoinStream(bufferStream, exportStream));
            }

            return new JoinStream(streams);
        }

        public override int GetHashCode()
        {
            if (this.ProtectedPages.Count == 0) return 0;
            else return this.ProtectedPages[0].GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is DocumentContent)) return false;

            return this.Equals((DocumentContent)obj);
        }

        public override bool Equals(DocumentContent other)
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

        public override DocumentContent DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return DocumentContent.Import(stream, BufferManager.Instance);
            }
        }

        protected override Stream GetCertificateStream()
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

        public override Certificate Certificate
        {
            get
            {
                return _certificate;
            }
            protected set
            {
                _certificate = value;
            }
        }

        #region IDocument<Page>

        public IEnumerable<Page> Pages
        {
            get
            {
                return this.ProtectedPages;
            }
        }

        [DataMember(Name = "Pages")]
        private PageCollection ProtectedPages
        {
            get
            {
                if (_pages == null)
                    _pages = new PageCollection(DocumentContent.MaxPagesCount);

                return _pages;
            }
        }

        #endregion
    }
}

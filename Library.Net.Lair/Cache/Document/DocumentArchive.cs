using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Lair
{
    [DataContract(Name = "DocumentArchive", Namespace = "http://Library/Net/Lair")]
    public sealed class DocumentArchive : ItemBase<DocumentArchive>, IDocumentArchive<DocumentPage>
    {
        private enum SerializeId : byte
        {
            DocumentPage = 0,
        }

        private DocumentPageCollection _documentPages = null;

        public static readonly int MaxDocumentPageCount = 256;

        public DocumentArchive(IEnumerable<DocumentPage> documentPages)
        {
            if (documentPages != null) this.ProtectedDocumentPages.AddRange(documentPages);
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
                    if (id == (byte)SerializeId.DocumentPage)
                    {
                        this.ProtectedDocumentPages.Add(DocumentPage.Import(rangeStream, bufferManager));
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            List<Stream> streams = new List<Stream>();
            Encoding encoding = new UTF8Encoding(false);

            // DocumentPages
            foreach (var d in this.Pages)
            {
                Stream exportStream = d.Export(bufferManager);

                BufferStream bufferStream = new BufferStream(bufferManager);
                bufferStream.Write(NetworkConverter.GetBytes((int)exportStream.Length), 0, 4);
                bufferStream.WriteByte((byte)SerializeId.DocumentPage);

                streams.Add(new JoinStream(bufferStream, exportStream));
            }

            return new JoinStream(streams);
        }

        public override int GetHashCode()
        {
            if (this.ProtectedDocumentPages.Count == 0) return 0;
            else return this.ProtectedDocumentPages[0].GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is DocumentArchive)) return false;

            return this.Equals((DocumentArchive)obj);
        }

        public override bool Equals(DocumentArchive other)
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

        public override DocumentArchive DeepClone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return DocumentArchive.Import(stream, BufferManager.Instance);
            }
        }

        #region IDocumentSiteContent<DocumentPage>

        public IEnumerable<DocumentPage> Pages
        {
            get
            {
                return this.ProtectedDocumentPages;
            }
        }

        [DataMember(Name = "DocumentPages")]
        private DocumentPageCollection ProtectedDocumentPages
        {
            get
            {
                if (_documentPages == null)
                    _documentPages = new DocumentPageCollection(DocumentArchive.MaxDocumentPageCount);

                return _documentPages;
            }
        }

        #endregion
    }
}

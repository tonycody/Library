using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "WikiDocumentContent", Namespace = "http://Library/Net/Outopos")]
    public sealed class WikiDocumentContent : ItemBase<WikiDocumentContent>
    {
        private enum SerializeId : byte
        {
            WikiPage = 0,
        }

        private WikiPageCollection _wikiPages;

        public static readonly int MaxWikiPageCount = 256;

        public WikiDocumentContent(IEnumerable<WikiPage> wikiPages)
        {
            if (wikiPages != null) this.ProtectedWikiPages.AddRange(wikiPages);
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            byte[] lengthBuffer = new byte[4];

            for (; ; )
            {
                if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                int length = NetworkConverter.ToInt32(lengthBuffer);
                byte id = (byte)stream.ReadByte();

                using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                {
                    if (id == (byte)SerializeId.WikiPage)
                    {
                        this.ProtectedWikiPages.Add(WikiPage.Import(rangeStream, bufferManager));
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            BufferStream bufferStream = new BufferStream(bufferManager);

            // WikiPages
            foreach (var value in this.WikiPages)
            {
                using (var stream = value.Export(bufferManager))
                {
                    ItemUtilities.Write(bufferStream, (byte)SerializeId.WikiPage, stream);
                }
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
        }

        public override int GetHashCode()
        {
            if (this.ProtectedWikiPages.Count == 0) return 0;
            else return this.ProtectedWikiPages[0].GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is WikiDocumentContent)) return false;

            return this.Equals((WikiDocumentContent)obj);
        }

        public override bool Equals(WikiDocumentContent other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if ((this.WikiPages == null) != (other.WikiPages == null))
            {
                return false;
            }

            if (this.WikiPages != null && other.WikiPages != null)
            {
                if (!CollectionUtilities.Equals(this.WikiPages, other.WikiPages)) return false;
            }

            return true;
        }

        private volatile ReadOnlyCollection<WikiPage> _readOnlyWikiPages;

        public IEnumerable<WikiPage> WikiPages
        {
            get
            {
                if (_readOnlyWikiPages == null)
                    _readOnlyWikiPages = new ReadOnlyCollection<WikiPage>(this.ProtectedWikiPages.ToArray());

                return _readOnlyWikiPages;
            }
        }

        [DataMember(Name = "WikiPages")]
        private WikiPageCollection ProtectedWikiPages
        {
            get
            {
                if (_wikiPages == null)
                    _wikiPages = new WikiPageCollection(WikiDocumentContent.MaxWikiPageCount);

                return _wikiPages;
            }
        }
    }
}

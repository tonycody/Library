using System;

namespace Library.Net.Lair
{
    interface IDocumentArchiveHeader<TDocument> : IHeader<TDocument>
        where TDocument : IDocument
    {

    }
}

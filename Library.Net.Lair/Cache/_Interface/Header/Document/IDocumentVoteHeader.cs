using System;

namespace Library.Net.Lair
{
    interface IDocumentVoteHeader<TDocument> : IHeader<TDocument>
        where TDocument : IDocument
    {

    }
}

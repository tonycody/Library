using System;

namespace Library.Net.Lair
{
    interface IDocumentOpinions<TDocument, TKey> : IComputeHash
        where TDocument : IDocument
        where TKey : IKey
    {
        TDocument Document { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}

using System;

namespace Library.Net.Lair
{
    interface IDocumentOpinion<TDocument, TKey> : IComputeHash
        where TDocument : IDocument
        where TKey : IKey
    {
        TDocument Document { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}

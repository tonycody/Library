using System;

namespace Library.Net.Lair
{
    interface IDocumentPage<TDocument, TKey> : IComputeHash
        where TDocument : IDocument
        where TKey : IKey
    {
        TDocument Document { get; }
        string Name { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}

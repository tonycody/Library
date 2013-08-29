using System;

namespace Library.Net.Lair
{
    interface IDocument<TSection, TKey> : IComputeHash
        where TSection : ISection
        where TKey : IKey
    {
        TSection Section { get; }
        string Name { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}

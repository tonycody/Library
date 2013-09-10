using System;

namespace Library.Net.Lair
{
    interface ISectionProfile<TSection, TKey> : IComputeHash
        where TSection : ISection
        where TKey : IKey
    {
        TSection Section { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}

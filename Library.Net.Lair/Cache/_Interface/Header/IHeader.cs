using System;

namespace Library.Net.Lair
{
    interface IHeader<TLink, TTag> : IComputeHash
        where TLink : ILink<TTag>
        where TTag : ITag
    {
        TLink Link { get; }
        string Type { get; }
        DateTime CreationTime { get; }
        byte[] Content { get; }
    }
}

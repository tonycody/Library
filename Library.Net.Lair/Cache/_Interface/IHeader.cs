using System;

namespace Library.Net.Lair
{
    public interface IHeader<TTag> : IComputeHash
        where TTag : ITag
    {
        TTag Tag { get; }
        DateTime CreationTime { get; }
        Key Key { get; }
    }
}

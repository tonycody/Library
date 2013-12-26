using System;

namespace Library.Net.Lair
{
    interface IHeader<TTag> : IComputeHash
        where TTag : ITag
    {
        TTag Tag { get; }
        DateTime CreationTime { get; }
        Key Key { get; }
    }
}

using System;

namespace Library.Net.Outopos
{
    interface IHeader<TTag, TKey>
        where TTag : ITag
        where TKey : IKey
    {
        TTag Tag { get; }
        DateTime CreationTime { get; }
        TKey Key { get; }
    }
}

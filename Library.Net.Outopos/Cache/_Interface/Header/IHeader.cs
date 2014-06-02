using System;

namespace Library.Net.Outopos
{
    public interface IHeader<TTag, TKey> : IComputeHash
        where TTag : ITag
        where TKey : IKey
    {
        TTag Tag { get; }
        DateTime CreationTime { get; }
        TKey Key { get; }
    }
}

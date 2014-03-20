using System;

namespace Library.Net.Outopos
{
    interface IHeader<TKey> : IComputeHash
        where TKey : IKey
    {
        DateTime CreationTime { get; }
        TKey Key { get; }
    }
}

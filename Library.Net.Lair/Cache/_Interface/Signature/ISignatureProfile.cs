using System;

namespace Library.Net.Lair
{
    interface ISignatureProfile<TKey> : IComputeHash
        where TKey : IKey
    {
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}

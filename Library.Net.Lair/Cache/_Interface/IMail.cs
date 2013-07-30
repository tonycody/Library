using System;

namespace Library.Net.Lair
{
    interface IMail<TKey> : IComputeHash
        where TKey : IKey
    {
        string RecipientSignature { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}

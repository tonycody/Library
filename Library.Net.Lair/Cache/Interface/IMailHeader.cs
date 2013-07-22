using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IMailHeader<TKey> : IComputeHash
        where TKey : IKey
    {
        string RecipientSignature { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}

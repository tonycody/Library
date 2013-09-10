using System;

namespace Library.Net.Lair
{
    interface IChatMessage<TChat, TKey> : IComputeHash
        where TChat : IChat
        where TKey : IKey
    {
        TChat Chat { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}

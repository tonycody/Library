using System;

namespace Library.Net.Lair
{
    interface IChatTopic<TChat, TKey> : IComputeHash
        where TChat : IChat
        where TKey : IKey
    {
        TChat Chat { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}

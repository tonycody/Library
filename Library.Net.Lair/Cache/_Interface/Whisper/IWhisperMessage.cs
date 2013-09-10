using System;

namespace Library.Net.Lair
{
    interface IWhisperMessage<TWhisper, TKey> : IComputeHash
        where TWhisper : IWhisper
        where TKey : IKey
    {
        TWhisper Whisper { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}

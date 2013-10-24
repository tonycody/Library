using System;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IHeader<TTag, TKey> : IComputeHash
        where TTag : ITag
        where TKey : IKey
    {
        TTag Tag { get; }
        string Type { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}

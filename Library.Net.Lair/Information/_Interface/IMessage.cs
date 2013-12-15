using System;

namespace Library.Net.Lair
{
    public interface IMessage<TTag>
        where TTag : ITag
    {
        TTag Tag { get; }
        string Signature { get; }
        DateTime CreationTime { get; }
    }
}

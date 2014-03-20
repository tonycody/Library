using System;

namespace Library.Net.Outopos
{
    public interface IMessage<out TTag>
        where TTag : ITag
    {
        TTag Tag { get; }
        string Signature { get; }
        DateTime CreationTime { get; }
    }
}

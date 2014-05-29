using System;

namespace Library.Net.Outopos
{
    public interface IMulticastHeader<TTag> : IComputeHash
        where TTag : ITag
    {
        TTag Tag { get; }
        DateTime CreationTime { get; }
        Key Key { get; }
        byte[] Option { get; }
    }
}

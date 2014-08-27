using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IMulticastMetadata<TTag> : IComputeHash
        where TTag : ITag
    {
        TTag Tag { get; }
        DateTime CreationTime { get; }
        Key Key { get; }
        int Cost { get; }
    }
}

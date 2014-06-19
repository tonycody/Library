using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IMulticastHeader<TTag> : IComputeHash
        where TTag : ITag
    {
        TTag Tag { get; }
        DateTime CreationTime { get; }
        Key Key { get; }
        int Coin { get; }
    }
}

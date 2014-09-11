using System;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IMulticastHeader<TTag>
        where TTag : ITag
    {
        TTag Tag { get; }
        DateTime CreationTime { get; }
    }
}

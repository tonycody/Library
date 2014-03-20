using System;
using System.Collections.Generic;

namespace Library.Net.Outopos
{
    interface IIndex<TTag, THeader>
        where TTag : ITag
        where THeader : IHeader
    {
        TTag Tag { get; }
        IEnumerable<THeader> Headers { get; }
    }
}

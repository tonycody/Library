using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface ILink<TTag>
        where TTag : ITag
    {
        TTag Tag { get; }
        IEnumerable<string> Options { get; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Lair
{
    interface IProfileHeader<TSection, TKey> : IComputeHash
        where TSection : ISection
        where TKey : IKey
    {
        TSection Section { get; }
        DateTime CreationTime { get; }
        TKey Content { get; }
    }
}

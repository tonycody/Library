using System;
using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IManager<TSection> : IComputeHash
        where TSection : ISection
    {
        TSection Section { get; }
        DateTime CreationTime { get; }
        string Comment { get; }
        IEnumerable<string> TrustSignatures { get; }
    }
}

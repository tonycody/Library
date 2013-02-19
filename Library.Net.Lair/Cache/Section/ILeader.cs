using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Lair
{
    interface ILeader<TSection> : IComputeHash
        where TSection : ISection
    {
        TSection Section { get; }
        DateTime CreationTime { get; }
        string Comment { get; }
        IEnumerable<string> CreatorSignatures { get; }
        IEnumerable<string> ManagerSignatures { get; }
    }
}

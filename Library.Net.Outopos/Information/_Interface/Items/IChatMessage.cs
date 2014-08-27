using System;
using System.Collections.Generic;
using Library.Security;

namespace Library.Net.Outopos
{
    interface IChatMessage : IComputeHash
    {
        Chat Tag { get; }
        DateTime CreationTime { get; }
        string Comment { get; }
        IEnumerable<Anchor> Anchors { get; }
    }
}

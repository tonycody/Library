using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IChatMessageContent : IComputeHash
    {
        string Comment { get; }
        IEnumerable<Anchor> Anchors { get; }
    }
}

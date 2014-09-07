using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Library.Security;

namespace Library.Net.Outopos
{
    public interface IChatMessageContent
    {
        string Comment { get; }
        IEnumerable<Anchor> Anchors { get; }
    }
}

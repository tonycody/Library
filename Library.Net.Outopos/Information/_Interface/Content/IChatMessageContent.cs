using System.Collections.Generic;

namespace Library.Net.Outopos
{
    interface IChatMessageContent<TAnchor>
        where TAnchor : IAnchor
    {
        string Comment { get; }
        IEnumerable<TAnchor> Anchors { get; }
    }
}

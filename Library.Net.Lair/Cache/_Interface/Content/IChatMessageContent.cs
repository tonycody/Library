using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IChatMessageContent<TAnchor>
        where TAnchor : IAnchor
    {
        string Comment { get; }
        IEnumerable<TAnchor> Anchors { get; }
    }
}

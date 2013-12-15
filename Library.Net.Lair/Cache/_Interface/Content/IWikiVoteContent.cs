using System.Collections.Generic;

namespace Library.Net.Lair
{
    interface IWikiVoteContent<TAnchor>
        where TAnchor : IAnchor
    {
        IEnumerable<TAnchor> Goods { get; }
        IEnumerable<TAnchor> Bads { get; }
    }
}

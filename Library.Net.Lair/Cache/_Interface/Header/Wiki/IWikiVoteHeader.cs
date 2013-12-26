using System;

namespace Library.Net.Lair
{
    interface IWikiVoteHeader<TWiki> : IHeader<TWiki>
        where TWiki : IWiki
    {

    }
}

using System;

namespace Library.Net.Lair
{
    interface IWikiPageHeader<TWiki> : IHeader<TWiki>
        where TWiki : IWiki
    {

    }
}

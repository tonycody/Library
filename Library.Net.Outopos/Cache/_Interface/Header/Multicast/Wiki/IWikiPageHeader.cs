using System;

namespace Library.Net.Outopos
{
    interface IWikiPageHeader<TWiki> : IMulticastHeader<TWiki>
        where TWiki : IWiki
    {

    }
}

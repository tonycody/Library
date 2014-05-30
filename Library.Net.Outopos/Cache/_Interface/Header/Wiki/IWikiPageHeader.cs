using System;

namespace Library.Net.Outopos
{
    interface IWikiPageHeader<TWiki> : IHeader<TWiki>
        where TWiki : IWiki
    {

    }
}

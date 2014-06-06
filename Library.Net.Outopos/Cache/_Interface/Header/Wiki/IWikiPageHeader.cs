using System;

namespace Library.Net.Outopos
{
    interface IWikiPageHeader<TMetadata, TWiki> : IHeader<TMetadata, TWiki>
        where TMetadata : IMetadata<TWiki>
        where TWiki : IWiki
    {

    }
}

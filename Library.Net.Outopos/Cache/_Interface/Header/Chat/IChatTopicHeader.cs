using System;

namespace Library.Net.Outopos
{
    interface IChatTopicHeader<TMetadata, TChat> : IHeader<TMetadata, TChat>
        where TMetadata : IMetadata<TChat>
        where TChat : IChat
    {

    }
}

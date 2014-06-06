using System;

namespace Library.Net.Outopos
{
    interface IChatMessageHeader<TMetadata, TChat> : IHeader<TMetadata, TChat>
        where TMetadata : IMetadata<TChat>
        where TChat : IChat
    {

    }
}

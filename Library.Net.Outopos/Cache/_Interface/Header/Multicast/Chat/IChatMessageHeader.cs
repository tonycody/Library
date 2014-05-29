using System;

namespace Library.Net.Outopos
{
    interface IChatMessageHeader<TChat> : IMulticastHeader<TChat>
        where TChat : IChat
    {

    }
}

using System;

namespace Library.Net.Outopos
{
    interface IChatTopicHeader<TChat> : IMulticastHeader<TChat>
        where TChat : IChat
    {

    }
}

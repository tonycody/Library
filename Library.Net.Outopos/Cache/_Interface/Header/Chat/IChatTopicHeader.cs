using System;

namespace Library.Net.Outopos
{
    interface IChatTopicHeader<TChat> : IHeader<TChat>
        where TChat : IChat
    {

    }
}

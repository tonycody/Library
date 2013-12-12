using System;

namespace Library.Net.Lair
{
    interface IChatTopicHeader<TChat> : IHeader<TChat>
        where TChat : IChat
    {

    }
}

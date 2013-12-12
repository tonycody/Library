using System;

namespace Library.Net.Lair
{
    interface IChatMessageHeader<TChat> : IHeader<TChat>
        where TChat : IChat
    {

    }
}

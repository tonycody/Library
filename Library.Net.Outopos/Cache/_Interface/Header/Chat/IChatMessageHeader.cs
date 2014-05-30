using System;

namespace Library.Net.Outopos
{
    interface IChatMessageHeader<TChat> : IHeader<TChat>
        where TChat : IChat
    {

    }
}

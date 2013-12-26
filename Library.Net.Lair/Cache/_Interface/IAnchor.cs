using System.Collections.Generic;
using System;

namespace Library.Net.Lair
{
    interface IAnchor
    {
        bool Check<TMessage>(TMessage message)
            where TMessage : IMessage<ITag>;

        string Signature { get; }
        DateTime CreationTime { get; }
    }
}

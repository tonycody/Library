using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Outopos
{
    [DataContract(Name = "Chat", Namespace = "http://Library/Net/Outopos")]
    public sealed class Chat : Tag<Chat>
    {
        public Chat(string name, byte[] id)
            : base(name, id)
        {

        }
    }
}

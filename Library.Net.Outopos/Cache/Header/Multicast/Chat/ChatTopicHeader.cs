using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "ChatTopicHeader", Namespace = "http://Library/Net/Outopos")]
    public sealed class ChatTopicHeader : MulticastHeaderBase<ChatTopicHeader, Chat>
    {

    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "ChatTopicMetadata", Namespace = "http://Library/Net/Outopos")]
    public sealed class ChatTopicMetadata : Metadata<ChatTopicMetadata, Chat>
    {
        public ChatTopicMetadata(Chat tag, string signature, DateTime creationTime, Key key, Miner miner)
            : base(tag, signature, creationTime, key, miner)
        {

        }
    }

    [DataContract(Name = "ChatTopicHeader", Namespace = "http://Library/Net/Outopos")]
    public sealed class ChatTopicHeader : Header<ChatTopicHeader, ChatTopicMetadata, Chat>
    {
        public ChatTopicHeader(ChatTopicMetadata metadata, DigitalSignature digitalSignature)
            : base(metadata, digitalSignature)
        {

        }
    }
}

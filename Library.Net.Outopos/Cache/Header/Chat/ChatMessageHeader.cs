using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "ChatMessageMetadata", Namespace = "http://Library/Net/Outopos")]
    public sealed class ChatMessageMetadata : Metadata<ChatMessageMetadata, Chat>
    {
        public ChatMessageMetadata(Chat tag, string signature, DateTime creationTime, Key key, Miner miner)
            : base(tag, signature, creationTime, key, miner)
        {

        }
    }

    [DataContract(Name = "ChatMessageHeader", Namespace = "http://Library/Net/Outopos")]
    public sealed class ChatMessageHeader : Header<ChatMessageHeader, ChatMessageMetadata, Chat>
    {
        public ChatMessageHeader(ChatMessageMetadata metadata, DigitalSignature digitalSignature)
            : base(metadata, digitalSignature)
        {

        }
    }
}

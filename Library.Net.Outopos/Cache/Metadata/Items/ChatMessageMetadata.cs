using System;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "ChatMessageMetadata", Namespace = "http://Library/Net/Outopos")]
    sealed class ChatMessageMetadata : MulticastMetadata<ChatMessageMetadata, Chat>
    {
        public ChatMessageMetadata(Chat tag, DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
            : base(tag, creationTime, key, miner, digitalSignature)
        {

        }
    }
}

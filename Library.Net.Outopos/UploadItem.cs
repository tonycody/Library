using System;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "UploadItem", Namespace = "http://Library/Net/Outopos")]
    sealed class UploadItem
    {
        [DataMember(Name = "Type")]
        public string Type { get; set; }

        [DataMember(Name = "Signature")]
        public string Signature { get; set; }

        [DataMember(Name = "Wiki")]
        public Wiki Wiki { get; set; }

        [DataMember(Name = "Chat")]
        public Chat Chat { get; set; }

        [DataMember(Name = "CreationTime")]
        public DateTime CreationTime { get; set; }

        [DataMember(Name = "MiningLimit")]
        public int MiningLimit { get; set; }

        [DataMember(Name = "DigitalSignature")]
        public DigitalSignature DigitalSignature { get; set; }

        [DataMember(Name = "ExchangePublicKey")]
        public ExchangePublicKey ExchangePublicKey { get; set; }

        [DataMember(Name = "Profile")]
        public Profile Profile { get; set; }

        [DataMember(Name = "SignatureMessage")]
        public SignatureMessage SignatureMessage { get; set; }

        [DataMember(Name = "WikiPage")]
        public WikiPage WikiPage { get; set; }

        [DataMember(Name = "ChatTopic")]
        public ChatTopic ChatTopic { get; set; }

        [DataMember(Name = "ChatMessage")]
        public ChatMessage ChatMessage { get; set; }
    }
}

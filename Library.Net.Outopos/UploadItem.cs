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

        [DataMember(Name = "DigitalSignature")]
        public DigitalSignature DigitalSignature { get; set; }

        [DataMember(Name = "MiningLimit")]
        public int MiningLimit { get; set; }

        [DataMember(Name = "MiningTime")]
        public TimeSpan MiningTime { get; set; }

        [DataMember(Name = "ExchangePublicKey")]
        public ExchangePublicKey ExchangePublicKey { get; set; }

        [DataMember(Name = "ProfileContent")]
        public ProfileContent ProfileContent { get; set; }

        [DataMember(Name = "SignatureMessageContent")]
        public SignatureMessageContent SignatureMessageContent { get; set; }

        [DataMember(Name = "WikiDocumentContent")]
        public WikiDocumentContent WikiDocumentContent { get; set; }

        [DataMember(Name = "ChatTopicContent")]
        public ChatTopicContent ChatTopicContent { get; set; }

        [DataMember(Name = "ChatMessageContent")]
        public ChatMessageContent ChatMessageContent { get; set; }
    }
}

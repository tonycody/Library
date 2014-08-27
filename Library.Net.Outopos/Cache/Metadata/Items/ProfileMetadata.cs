using System;
using System.Runtime.Serialization;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "ProfileMetadata", Namespace = "http://Library/Net/Outopos")]
    sealed class ProfileMetadata : BroadcastMetadata<ProfileMetadata>
    {
        public ProfileMetadata(DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
            : base(creationTime, key, miner, digitalSignature)
        {

        }
    }
}

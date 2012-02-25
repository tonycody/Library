using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "HashAlgorithm", Namespace = "http://Library/Net/Amoeba")]
    public enum HashAlgorithm
    {
        [EnumMember(Value = "Sha512")]
        Sha512 = 0,
    }

    interface IHashAlgorithm
    {
        HashAlgorithm HashAlgorithm { get; }
    }
}

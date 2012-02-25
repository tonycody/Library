using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "CompressionAlgorithm", Namespace = "http://Library/Net/Amoeba")]
    public enum CompressionAlgorithm
    {
        [EnumMember(Value = "None")]
        None = 0,

        [EnumMember(Value = "XZ")]
        XZ = 1,
    }

    interface ICompressionAlgorithm
    {
        CompressionAlgorithm CompressionAlgorithm { get; }
    }
}

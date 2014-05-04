using System.Runtime.Serialization;

namespace Library.Net.Connections
{
    [DataContract(Name = "SecureConnectionType", Namespace = "http://Library/Net/Connection")]
    public enum SecureConnectionType
    {
        [EnumMember(Value = "In")]
        In = 0,

        [EnumMember(Value = "Out")]
        Out = 1,
    }
}

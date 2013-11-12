using System.Runtime.Serialization;

namespace Library.Net.Connections
{
    [DataContract(Name = "SecureConnectionType", Namespace = "http://Library/Net/Connection")]
    public enum SecureConnectionType
    {
        [EnumMember(Value = "Client")]
        Client = 0,

        [EnumMember(Value = "Server")]
        Server = 1,
    }
}

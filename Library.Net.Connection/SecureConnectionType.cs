using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Net.Connection
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

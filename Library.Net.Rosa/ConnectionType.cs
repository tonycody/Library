using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.IO;
using System.Xml;
using Library;

namespace Library.Net.Rosa
{
    [DataContract(Name = "ConnectionType", Namespace = "http://Library/Net/Amoeba")]
    public enum ConnectionType
    {
        [EnumMember(Value = "Tcp")]
        Tcp = 0,

        [EnumMember(Value = "Socks4Proxy")]
        Socks4Proxy = 1,

        [EnumMember(Value = "Socks4aProxy")]
        Socks4aProxy = 2,

        [EnumMember(Value = "Socks5Proxy")]
        Socks5Proxy = 3,

        [EnumMember(Value = "HttpProxy")]
        HttpProxy = 4,
    }
}

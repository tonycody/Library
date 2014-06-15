using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Outopos
{
    [DataContract(Name = "Wiki", Namespace = "http://Library/Net/Outopos")]
    public sealed class Wiki : Tag<Wiki>
    {
        public Wiki(string name, byte[] id)
            : base(name, id)
        {

        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Net.Outopos
{
    [DataContract(Name = "Mail", Namespace = "http://Library/Net/Outopos")]
    public sealed class Mail : Tag<Mail>
    {
        public Mail(string name, byte[] id)
            : base(name, id)
        {

        }
    }
}

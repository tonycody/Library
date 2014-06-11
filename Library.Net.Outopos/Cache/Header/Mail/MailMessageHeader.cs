using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;
using Library.Security;

namespace Library.Net.Outopos
{
    [DataContract(Name = "MailMessageHeader", Namespace = "http://Library/Net/Outopos")]
    public sealed class MailMessageHeader : Header<MailMessageHeader, Mail>
    {
        public MailMessageHeader(Mail tag, DateTime creationTime, Key key, Miner miner, DigitalSignature digitalSignature)
            : base(tag, creationTime, key, miner, digitalSignature)
        {

        }
    }
}

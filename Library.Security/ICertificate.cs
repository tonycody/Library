using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Serialization;

namespace Library.Security
{
    public interface ICertificate
    {
        Certificate Certificate { get; }
    }
}

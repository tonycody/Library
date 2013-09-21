using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Security
{
    public interface IExchangeEncrypt : IExchangeAlgorithm
    {
        byte[] PublicKey { get; }
    }
}

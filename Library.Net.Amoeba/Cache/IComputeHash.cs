﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Amoeba
{
    interface IComputeHash
    {
        byte[] GetHash(HashAlgorithm hashAlgorithm);
        bool VerifyHash(byte[] hash, HashAlgorithm hashAlgorithm);
    }
}
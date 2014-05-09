using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Library.Security
{
    class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[] x, byte[] y)
        {
            if ((x == null) != (y == null)) return false;
            if (object.ReferenceEquals(x, y)) return true;

            return Unsafe.Equals(x, y);
        }

        public int GetHashCode(byte[] value)
        {
            return ItemUtilities.GetHashCode(value);
        }
    }
}

using System;
using System.Collections.Generic;

namespace Library.Net.Amoeba
{
    sealed class BytesEqualityComparer : IEqualityComparer<byte[]>
    {
        #region IEqualityComparer<byte[]>

        public bool Equals(byte[] x, byte[] y)
        {
            if (x.Length != y.Length) return false;

            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] != y[i])
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(byte[] obj)
        {
            if (obj != null && obj.Length != 0)
            {
                if (obj.Length >= 4) return BitConverter.ToInt32(obj, 0) & 0x7FFFFFFF;
                else if (obj.Length >= 2) return BitConverter.ToUInt16(obj, 0);
                else return obj[0];
            }
            else
            {
                return 0;
            }
        }

        #endregion
    }
}

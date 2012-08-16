using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Lair
{
    internal class BytesEqualityComparer : IEqualityComparer<byte[]>
    {
        #region IEqualityComparer<byte[]>

        public bool Equals(byte[] x, byte[] y)
        {
            if ((x == null) != (y == null)) return false;

            if (x != null && y != null)
            {
                if (!Collection.Equals(x, y))
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
                if (obj.Length >= 2) return BitConverter.ToUInt16(obj, 0);
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

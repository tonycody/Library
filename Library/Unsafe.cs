using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library
{
    public static class Unsafe
    {
        public new static bool Equals(object obj1, object obj2)
        {
            throw new NotImplementedException();
        }

        // Copyright (c) 2008-2013 Hafthor Stefansson
        // Distributed under the MIT/X11 software license
        // Ref: http://www.opensource.org/licenses/mit-license.php.
        public static unsafe bool Equals(byte[] x, byte[] y)
        {
            if (x.Length != y.Length) return false;

            fixed (byte* p_x = x, p_y = y)
            {
                byte* t_x = p_x, t_y = p_y;
                int length = x.Length;

                for (int i = (length / 8) - 1; i >= 0; i--, t_x += 8, t_y += 8)
                {
                    if (*((long*)t_x) != *((long*)t_y)) return false;
                }

                if ((length & 4) != 0)
                {
                    if (*((int*)t_x) != *((int*)t_y)) return false;
                    t_x += 4; t_y += 4;
                }

                if ((length & 2) != 0)
                {
                    if (*((short*)t_x) != *((short*)t_y)) return false;
                    t_x += 2; t_y += 2;
                }

                if ((length & 1) != 0)
                {
                    if (*((byte*)t_x) != *((byte*)t_y)) return false;
                }

                return true;
            }
        }
    }
}

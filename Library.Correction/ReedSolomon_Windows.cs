using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;

namespace Library.Correction
{
    public class ReedSolomon : ManagerBase
    {
        private ReedSolomon_Utility.FEC _fecMath;
        private object _thisLock = new object();
        private volatile bool _disposed = false;

        public ReedSolomon(int k, int n)
        {
            _fecMath = new ReedSolomon_Utility.FEC(k, n);
        }

        public void Encode(byte[][] src, byte[][] repair, int[] index, int size)
        {
            _fecMath.Encode(src, repair, index, size);
        }

        public void Decode(byte[][] pkts, int[] index, int size)
        {
            _fecMath.Decode(pkts, index, size);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                try
                {
                    _fecMath.Dispose();
                }
                catch (Exception)
                {

                }
            }

            _disposed = true;
        }
    }
}

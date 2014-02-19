using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Library.Correction
{
    public class ReedSolomon8 : ManagerBase
    {
        private static Assembly _asm;
        private static object _lockObject = new object();
        private dynamic _instance = null;

        private volatile bool _disposed;

        static ReedSolomon8()
        {
            try
            {
                if (System.Environment.Is64BitProcess)
                {
                    _asm = Assembly.LoadFrom("Assembly/ReedSolomon_x64.dll");
                }
                else
                {
                    _asm = Assembly.LoadFrom("Assembly/ReedSolomon_x86.dll");
                }
            }
            catch (Exception e)
            {
                Log.Warning(e);
            }
        }

        public ReedSolomon8(int k, int n)
        {
            lock (_lockObject)
            {
                _instance = Activator.CreateInstance(_asm.GetType("ReedSolomon.FEC"), k, n);
            }
        }

        public void Encode(byte[][] src, byte[][] repair, int[] index, int size)
        {
            _instance.Encode(src, repair, index, size);
        }

        public void Decode(byte[][] pkts, int[] index, int size)
        {
            _instance.Decode(pkts, index, size);
        }

        public void Cancel()
        {
            _instance.Cancel();
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_instance != null)
                {
                    try
                    {
                        _instance.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _instance = null;
                }
            }
        }
    }
}

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
        private volatile bool _nativeFlag = false;

        private static Assembly _asm;
        private static object _lockObject = new object();
        private dynamic _native = null;

        private ReedSolomon _managed;

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
            catch (Exception)
            {

            }
        }

        public ReedSolomon8(int k, int n)
        {
            lock (_lockObject)
            {
                try
                {
                    _native = Activator.CreateInstance(_asm.GetType("ReedSolomon.FEC"), k, n);
                    _nativeFlag = true;
                }
                catch (Exception)
                {
                    var threadCount = Math.Max(1, Math.Min(System.Environment.ProcessorCount, 32) / 2);
                    _managed = new ReedSolomon(8, k, n, threadCount, BufferManager.Instance);
                }
            }
        }

        internal ReedSolomon8(int k, int n, bool nativeFlag)
        {
            lock (_lockObject)
            {
                if (nativeFlag)
                {
                    _native = Activator.CreateInstance(_asm.GetType("ReedSolomon.FEC"), k, n);
                    _nativeFlag = true;
                }
                else
                {
                    var threadCount = Math.Max(1, Math.Min(System.Environment.ProcessorCount, 32) / 2);
                    _managed = new ReedSolomon(8, k, n, threadCount, BufferManager.Instance);
                }
            }
        }

        public void Encode(byte[][] src, byte[][] repair, int[] index, int size)
        {
            if (_nativeFlag)
            {
                _native.Encode(src, repair, index, size);
            }
            else
            {
                var srcList = new ArraySegment<byte>[src.Length];
                var repairList = new ArraySegment<byte>[repair.Length];

                for (int i = 0; i < srcList.Length; i++)
                {
                    srcList[i] = new ArraySegment<byte>(src[i], 0, size);
                }

                for (int i = 0; i < repairList.Length; i++)
                {
                    repairList[i] = new ArraySegment<byte>(repair[i], 0, size);
                }

                _managed.Encode(srcList, repairList, index, size);

                for (int i = 0; i < src.Length; i++)
                {
                    src[i] = srcList[i].Array;
                }

                for (int i = 0; i < repair.Length; i++)
                {
                    repair[i] = repairList[i].Array;
                }
            }
        }

        public void Decode(byte[][] pkts, int[] index, int size)
        {
            if (_nativeFlag)
            {
                _native.Decode(pkts, index, size);
            }
            else
            {
                var pktsList = new ArraySegment<byte>[pkts.Length];

                for (int i = 0; i < pktsList.Length; i++)
                {
                    pktsList[i] = new ArraySegment<byte>(pkts[i], 0, size);
                }

                _managed.Decode(pktsList, index, size);

                for (int i = 0; i < pkts.Length; i++)
                {
                    pkts[i] = pktsList[i].Array;
                }
            }
        }

        public void Cancel()
        {
            if (_nativeFlag)
            {
                _native.Cancel();
            }
            else
            {
                _managed.Cancel();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                if (_native != null)
                {
                    try
                    {
                        _native.Dispose();
                    }
                    catch (Exception)
                    {

                    }

                    _native = null;
                }
            }
        }
    }
}

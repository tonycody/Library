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
                IList<ArraySegment<byte>> srcList = new List<ArraySegment<byte>>();
                IList<ArraySegment<byte>> repairList = new List<ArraySegment<byte>>();

                foreach (var value in src)
                {
                    srcList.Add(new ArraySegment<byte>(value, 0, size));
                }

                foreach (var value in repair)
                {
                    repairList.Add(new ArraySegment<byte>(value, 0, size));
                }

                _managed.Encode(srcList, repairList, index);
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
                IList<ArraySegment<byte>> pktsList = new List<ArraySegment<byte>>();

                foreach (var value in pkts)
                {
                    pktsList.Add(new ArraySegment<byte>(value, 0, size));
                }

                _managed.Decode(pktsList, index);

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

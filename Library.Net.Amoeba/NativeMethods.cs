using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Library.Net.Amoeba
{
    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_INFO
    {
        public int dwOemId;
        public int dwPageSize;
        public IntPtr lpMinimumApplicationAddress;
        public IntPtr lpMaximumApplicationAddress;
        public IntPtr dwActiveProcessorMask;
        public int dwNumberOfProcessors;
        public int dwProcessorType;
        public int dwAllocationGranularity;
        public short dwProcessorLevel;
        public short dwProcessorRevision;
    }

    static class NativeMethods
    {
        [DllImport("kernel32")]
        public static extern void GetSystemInfo(ref SYSTEM_INFO ptmpsi);
    }
}

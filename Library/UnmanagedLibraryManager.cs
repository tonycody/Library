using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Library
{
#if Mono

#else
    public class UnmanagedLibraryManager : ManagerBase
    {
        private volatile bool _disposed;

        IntPtr _moduleHandle = IntPtr.Zero;

        public UnmanagedLibraryManager(string path)
        {
            _moduleHandle = NativeMethods.LoadLibrary(path);
        }

        public T GetDelegate<T>(string method)
            where T : class
        {
            if (!typeof(T).IsSubclassOf(typeof(Delegate)))
            {
                throw new InvalidOperationException(typeof(T).Name + " is not a delegate type");
            }

            IntPtr methodHandle = NativeMethods.GetProcAddress(_moduleHandle, method);
            return Marshal.GetDelegateForFunctionPointer(methodHandle, typeof(T)) as T;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {

            }

            if (_moduleHandle != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.FreeLibrary(_moduleHandle);
                }
                catch (Exception)
                {

                }

                _moduleHandle = IntPtr.Zero;
            }
        }
    }
#endif
}

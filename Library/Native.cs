using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Library
{
    public unsafe static class Native
    {
#if Mono

#else
        private static NativeLibraryManager _nativeLibraryManager;

        [return: MarshalAs(UnmanagedType.U1)]
        private unsafe delegate bool EqualsDelegate(byte* source, byte* destination, int len);
        private unsafe delegate int CompareDelegate(byte* source, byte* destination, int len);
        private unsafe delegate void XorDelegate(byte* source, byte* destination, byte* result, int len);

        private static EqualsDelegate _equals;
        private static CompareDelegate _compare;
        private static XorDelegate _xor;
#endif

        static Native()
        {
#if Mono

#else
            try
            {
                if (System.Environment.Is64BitProcess)
                {
                    _nativeLibraryManager = new NativeLibraryManager("Assembly/Library_x64.dll");
                }
                else
                {
                    _nativeLibraryManager = new NativeLibraryManager("Assembly/Library_x86.dll");
                }

                _equals = _nativeLibraryManager.GetMethod<EqualsDelegate>("equals");
                _compare = _nativeLibraryManager.GetMethod<CompareDelegate>("compare");
                _xor = _nativeLibraryManager.GetMethod<XorDelegate>("xor");
            }
            catch (Exception e)
            {
                Log.Warning(e);
            }
#endif
        }

        public new static bool Equals(object obj1, object obj2)
        {
            throw new NotImplementedException();
        }

        public static void Copy(byte[] source, byte[] destination, int length)
        {
            //if (source == null) throw new ArgumentNullException("source");
            //if (destination == null) throw new ArgumentNullException("destination");

            //if (length > source.Length) throw new ArgumentOutOfRangeException("length");
            //if (length > destination.Length) throw new ArgumentOutOfRangeException("length");

            //if (length == 0) return;

            //fixed (byte* p_x = source)
            //{
            //    Marshal.Copy(new IntPtr((void*)p_x), destination, 0, length);
            //}

            Array.Copy(source, 0, destination, 0, length);
        }

        public static void Copy(byte[] source, int sourceIndex, byte[] destination, int destinationIndex, int length)
        {
            //if (source == null) throw new ArgumentNullException("source");
            //if (destination == null) throw new ArgumentNullException("destination");

            //if (0 > (source.Length - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            //if (0 > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");
            //if (length > (source.Length - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            //if (length > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            //if (length == 0) return;

            //fixed (byte* p_x = source)
            //{
            //    byte* t_x = p_x + sourceIndex;

            //    Marshal.Copy(new IntPtr((void*)t_x), destination, destinationIndex, length);
            //}

            Array.Copy(source, sourceIndex, destination, destinationIndex, length);
        }

        // Copyright (c) 2008-2013 Hafthor Stefansson
        // Distributed under the MIT/X11 software license
        // Ref: http://www.opensource.org/licenses/mit-license.php.
        // http://stackoverflow.com/questions/43289/comparing-two-byte-arrays-in-net
        public static bool Equals(byte[] source, byte[] destination)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");

            if (object.ReferenceEquals(source, destination)) return true;
            if (source.Length != destination.Length) return false;

            fixed (byte* p_x = source, p_y = destination)
            {
                int length = source.Length;

                return _equals(p_x, p_y, length);
            }
        }

        public static bool Equals(byte[] source, int sourceIndex, byte[] destination, int destinationIndex, int length)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");

            if (0 > (source.Length - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length > (source.Length - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            fixed (byte* p_x = source, p_y = destination)
            {
                byte* t_x = p_x + sourceIndex, t_y = p_y + destinationIndex;

                return _equals(t_x, t_y, length);
            }
        }

        public static int Compare(byte[] source, byte[] destination)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");

            if (source.Length != destination.Length) return (source.Length > destination.Length) ? 1 : -1;

            if (source.Length == 0) return 0;

            // ネイティブ呼び出しの前に、最低限の比較を行う。
            {
                int c;
                if ((c = source[0] - destination[0]) != 0) return c;
            }

            var length = source.Length - 1;
            if (length == 0) return 0;

            fixed (byte* p_x = source, p_y = destination)
            {
                byte* t_x = p_x + 1, t_y = p_y + 1;

                return _compare(t_x, t_y, length);
            }
        }

        internal static int Compare2(byte[] source, byte[] destination)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");

            if (source.Length != destination.Length) return (source.Length > destination.Length) ? 1 : -1;

            if (source.Length == 0) return 0;

            fixed (byte* p_x = source, p_y = destination)
            {
                return _compare(p_x, p_y, source.Length);
            }
        }

        public static int Compare(byte[] source, int sourceIndex, byte[] destination, int destinationIndex, int length)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");

            if (0 > (source.Length - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length > (source.Length - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            if (length == 0) return 0;

            // ネイティブ呼び出しの前に、最低限の比較を行う。
            {
                int c;
                if ((c = source[sourceIndex] - destination[destinationIndex]) != 0) return c;
            }

            length--;

            if (length == 0) return 0;

            fixed (byte* p_x = source, p_y = destination)
            {
                byte* t_x = p_x + sourceIndex + 1, t_y = p_y + destinationIndex + 1;

                return _compare(t_x, t_y, length);
            }
        }

        internal static int Compare2(byte[] source, int sourceIndex, byte[] destination, int destinationIndex, int length)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");

            if (0 > (source.Length - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length > (source.Length - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            if (length == 0) return 0;

            fixed (byte* p_x = source, p_y = destination)
            {
                byte* t_x = p_x + sourceIndex, t_y = p_y + destinationIndex;

                return _compare(t_x, t_y, length);
            }
        }

        public static byte[] Xor(byte[] source, byte[] destination)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");

            if (source.Length != destination.Length)
            {
                if (source.Length < destination.Length)
                {
                    fixed (byte* p_x = source, p_y = destination)
                    {
                        byte[] buffer = new byte[destination.Length];
                        int length = source.Length;

                        fixed (byte* p_buffer = buffer)
                        {
                            _xor(p_x, p_y, p_buffer, length);
                        }

                        Native.Copy(destination, source.Length, buffer, source.Length, destination.Length - source.Length);

                        return buffer;
                    }
                }
                else
                {
                    fixed (byte* p_x = source, p_y = destination)
                    {
                        byte[] buffer = new byte[source.Length];
                        int length = destination.Length;

                        fixed (byte* p_buffer = buffer)
                        {
                            _xor(p_x, p_y, p_buffer, length);
                        }

                        Native.Copy(source, destination.Length, buffer, destination.Length, source.Length - destination.Length);

                        return buffer;
                    }
                }
            }
            else
            {
                fixed (byte* p_x = source, p_y = destination)
                {
                    byte[] buffer = new byte[source.Length];
                    int length = source.Length;

                    fixed (byte* p_buffer = buffer)
                    {
                        _xor(p_x, p_y, p_buffer, length);
                    }

                    return buffer;
                }
            }
        }

        public static byte[] Xor(byte[] source, int sourceIndex, byte[] destination, int destinationIndex, int length)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (destination == null) throw new ArgumentNullException("destination");

            if (0 > (source.Length - sourceIndex)) throw new ArgumentOutOfRangeException("sourceIndex");
            if (0 > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length > (source.Length - sourceIndex)) throw new ArgumentOutOfRangeException("length");
            if (length > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            fixed (byte* p_x = source, p_y = destination)
            {
                byte* t_x = p_x + sourceIndex, t_y = p_y + destinationIndex;

                byte[] buffer = new byte[length];

                fixed (byte* p_buffer = buffer)
                {
                    _xor(t_x, t_y, p_buffer, length);
                }

                return buffer;
            }
        }
    }
}

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
        private unsafe delegate bool EqualsDelegate(byte* source1, byte* source2, int len);
        private unsafe delegate int CompareDelegate(byte* source1, byte* source2, int len);
        private unsafe delegate void XorDelegate(byte* source1, byte* source2, byte* result, int len);

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

        public static void Copy(byte[] source1, byte[] source2, int length)
        {
            if (source1 == null) throw new ArgumentNullException("source1");
            if (source2 == null) throw new ArgumentNullException("source2");

            if (length > source1.Length) throw new ArgumentOutOfRangeException("length");
            if (length > source2.Length) throw new ArgumentOutOfRangeException("length");

            if (length == 0) return;

            fixed (byte* p_x = source1)
            {
                Marshal.Copy(new IntPtr((void*)p_x), source2, 0, length);
            }

            //Array.Copy(source1, 0, source2, 0, length);
        }

        public static void Copy(byte[] source1, int source1Index, byte[] source2, int source2Index, int length)
        {
            if (source1 == null) throw new ArgumentNullException("source1");
            if (source2 == null) throw new ArgumentNullException("source2");

            if (0 > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException("source1Index");
            if (0 > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException("source2Index");
            if (length > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException("length");
            if (length > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException("length");

            if (length == 0) return;

            fixed (byte* p_x = source1)
            {
                byte* t_x = p_x + source1Index;

                Marshal.Copy(new IntPtr((void*)t_x), source2, source2Index, length);
            }

            //Array.Copy(source1, source1Index, source2, source2Index, length);
        }

        // Copyright (c) 2008-2013 Hafthor Stefansson
        // Distributed under the MIT/X11 software license
        // Ref: http://www.opensource.org/licenses/mit-license.php.
        // http://stackoverflow.com/questions/43289/comparing-two-byte-arrays-in-net
        public static bool Equals(byte[] source1, byte[] source2)
        {
            if (source1 == null) throw new ArgumentNullException("source1");
            if (source2 == null) throw new ArgumentNullException("source2");

            if (object.ReferenceEquals(source1, source2)) return true;
            if (source1.Length != source2.Length) return false;

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x, t_y = p_y;

                int length = source1.Length;

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

        public static bool Equals(byte[] source1, int source1Index, byte[] source2, int source2Index, int length)
        {
            if (source1 == null) throw new ArgumentNullException("source1");
            if (source2 == null) throw new ArgumentNullException("source2");

            if (0 > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException("source1Index");
            if (0 > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException("source2Index");
            if (length > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException("length");
            if (length > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException("length");

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x + source1Index, t_y = p_y + source2Index;

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

        public static int Compare(byte[] source1, byte[] source2)
        {
            if (source1 == null) throw new ArgumentNullException("source1");
            if (source2 == null) throw new ArgumentNullException("source2");

            if (source1.Length != source2.Length) return (source1.Length > source2.Length) ? 1 : -1;

            if (source1.Length == 0) return 0;

            // ネイティブ呼び出しの前に、最低限の比較を行う。
            {
                int c;
                if ((c = source1[0] - source2[0]) != 0) return c;
            }

            var length = source1.Length - 1;
            if (length == 0) return 0;

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x + 1, t_y = p_y + 1;

                return _compare(t_x, t_y, length);
            }
        }

        internal static int Compare2(byte[] source1, byte[] source2)
        {
            if (source1 == null) throw new ArgumentNullException("source1");
            if (source2 == null) throw new ArgumentNullException("source2");

            if (source1.Length != source2.Length) return (source1.Length > source2.Length) ? 1 : -1;

            if (source1.Length == 0) return 0;

            fixed (byte* p_x = source1, p_y = source2)
            {
                return _compare(p_x, p_y, source1.Length);
            }
        }

        public static int Compare(byte[] source1, int source1Index, byte[] source2, int source2Index, int length)
        {
            if (source1 == null) throw new ArgumentNullException("source1");
            if (source2 == null) throw new ArgumentNullException("source2");

            if (0 > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException("source1Index");
            if (0 > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException("source2Index");
            if (length > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException("length");
            if (length > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException("length");

            if (length == 0) return 0;

            // ネイティブ呼び出しの前に、最低限の比較を行う。
            {
                int c;
                if ((c = source1[source1Index] - source2[source2Index]) != 0) return c;
            }

            length--;

            if (length == 0) return 0;

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x + source1Index + 1, t_y = p_y + source2Index + 1;

                return _compare(t_x, t_y, length);
            }
        }

        internal static int Compare2(byte[] source1, int source1Index, byte[] source2, int source2Index, int length)
        {
            if (source1 == null) throw new ArgumentNullException("source1");
            if (source2 == null) throw new ArgumentNullException("source2");

            if (0 > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException("source1Index");
            if (0 > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException("source2Index");
            if (length > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException("length");
            if (length > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException("length");

            if (length == 0) return 0;

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x + source1Index, t_y = p_y + source2Index;

                return _compare(t_x, t_y, length);
            }
        }

        public static byte[] Xor(byte[] source1, byte[] source2)
        {
            if (source1 == null) throw new ArgumentNullException("source1");
            if (source2 == null) throw new ArgumentNullException("source2");

            byte[] buffer = new byte[Math.Max(source1.Length, source2.Length)];

            if (source1.Length < source2.Length)
            {
                Native.Copy(source2, source1.Length, buffer, source1.Length, source2.Length - source1.Length);
            }
            else if (source1.Length > source2.Length)
            {
                Native.Copy(source1, source2.Length, buffer, source2.Length, source1.Length - source2.Length);
            }

            int length = Math.Min(source1.Length, source2.Length);

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x, t_y = p_y;

                fixed (byte* p_buffer = buffer)
                {
                    byte* t_buffer = p_buffer;

                    for (int i = (length / 8) - 1; i >= 0; i--, t_x += 8, t_y += 8, t_buffer += 8)
                    {
                        *((long*)t_buffer) = *((long*)t_x) ^ *((long*)t_y);
                    }

                    if ((length & 4) != 0)
                    {
                        *((int*)t_buffer) = *((int*)t_x) ^ *((int*)t_y);
                        t_x += 4; t_y += 4; t_buffer += 4;
                    }

                    if ((length & 2) != 0)
                    {
                        *((short*)t_buffer) = (short)(*((short*)t_x) ^ *((short*)t_y));
                        t_x += 2; t_y += 2; t_buffer += 2;
                    }

                    if ((length & 1) != 0)
                    {
                        *((byte*)t_buffer) = (byte)(*((byte*)t_x) ^ *((byte*)t_y));
                    }
                }
            }

            return buffer;
        }

        public static void Xor(byte[] source1, byte[] source2, byte[] destination)
        {
            if (source1 == null) throw new ArgumentNullException("source1");
            if (source2 == null) throw new ArgumentNullException("source2");
            if (destination == null) throw new ArgumentNullException("destination");

            int length = Math.Min(source1.Length, source2.Length);
            length = Math.Min(length, destination.Length);

            if (length < destination.Length)
            {
                Array.Clear(destination, length, destination.Length - length);
            }

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x, t_y = p_y;

                fixed (byte* p_buffer = destination)
                {
                    byte* t_buffer = p_buffer;

                    for (int i = (length / 8) - 1; i >= 0; i--, t_x += 8, t_y += 8, t_buffer += 8)
                    {
                        *((long*)t_buffer) = *((long*)t_x) ^ *((long*)t_y);
                    }

                    if ((length & 4) != 0)
                    {
                        *((int*)t_buffer) = *((int*)t_x) ^ *((int*)t_y);
                        t_x += 4; t_y += 4; t_buffer += 4;
                    }

                    if ((length & 2) != 0)
                    {
                        *((short*)t_buffer) = (short)(*((short*)t_x) ^ *((short*)t_y));
                        t_x += 2; t_y += 2; t_buffer += 2;
                    }

                    if ((length & 1) != 0)
                    {
                        *((byte*)t_buffer) = (byte)(*((byte*)t_x) ^ *((byte*)t_y));
                    }
                }
            }
        }

        public static byte[] Xor(byte[] source1, int source1Index, byte[] source2, int source2Index, int length)
        {
            if (source1 == null) throw new ArgumentNullException("source1");
            if (source2 == null) throw new ArgumentNullException("source2");

            if (0 > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException("source1Index");
            if (0 > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException("source2Index");
            if (length > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException("length");
            if (length > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException("length");

            byte[] buffer = new byte[length];

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x + source1Index, t_y = p_y + source2Index;

                fixed (byte* p_buffer = buffer)
                {
                    byte* t_buffer = p_buffer;

                    for (int i = (length / 8) - 1; i >= 0; i--, t_x += 8, t_y += 8, t_buffer += 8)
                    {
                        *((long*)t_buffer) = *((long*)t_x) ^ *((long*)t_y);
                    }

                    if ((length & 4) != 0)
                    {
                        *((int*)t_buffer) = *((int*)t_x) ^ *((int*)t_y);
                        t_x += 4; t_y += 4; t_buffer += 4;
                    }

                    if ((length & 2) != 0)
                    {
                        *((short*)t_buffer) = (short)(*((short*)t_x) ^ *((short*)t_y));
                        t_x += 2; t_y += 2; t_buffer += 2;
                    }

                    if ((length & 1) != 0)
                    {
                        *((byte*)t_buffer) = (byte)(*((byte*)t_x) ^ *((byte*)t_y));
                    }
                }
            }

            return buffer;
        }

        public static void Xor(byte[] source1, int source1Index, byte[] source2, int source2Index, byte[] destination, int destinationIndex, int length)
        {
            if (source1 == null) throw new ArgumentNullException("source1");
            if (source2 == null) throw new ArgumentNullException("source2");
            if (destination == null) throw new ArgumentNullException("destination");

            if (0 > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException("source1Index");
            if (0 > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException("source2Index");
            if (0 > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException("destinationIndex");
            if (length > (source1.Length - source1Index)) throw new ArgumentOutOfRangeException("length");
            if (length > (source2.Length - source2Index)) throw new ArgumentOutOfRangeException("length");
            if (length > (destination.Length - destinationIndex)) throw new ArgumentOutOfRangeException("length");

            fixed (byte* p_x = source1, p_y = source2)
            {
                byte* t_x = p_x + source1Index, t_y = p_y + source2Index;

                fixed (byte* p_buffer = destination)
                {
                    byte* t_buffer = p_buffer;

                    for (int i = (length / 8) - 1; i >= 0; i--, t_x += 8, t_y += 8, t_buffer += 8)
                    {
                        *((long*)t_buffer) = *((long*)t_x) ^ *((long*)t_y);
                    }

                    if ((length & 4) != 0)
                    {
                        *((int*)t_buffer) = *((int*)t_x) ^ *((int*)t_y);
                        t_x += 4; t_y += 4; t_buffer += 4;
                    }

                    if ((length & 2) != 0)
                    {
                        *((short*)t_buffer) = (short)(*((short*)t_x) ^ *((short*)t_y));
                        t_x += 2; t_y += 2; t_buffer += 2;
                    }

                    if ((length & 1) != 0)
                    {
                        *((byte*)t_buffer) = (byte)(*((byte*)t_x) ^ *((byte*)t_y));
                    }
                }
            }
        }
    }
}

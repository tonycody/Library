using System;
using System.IO;
using Library.Io;
using NUnit.Framework;
using System.Collections.Generic;

namespace Library.UnitTest
{
    [TestFixture, Category("Library")]
    public class Test_Library
    {
        [Test]
        public void Test_Collection()
        {
            Assert.IsTrue(Collection.Equals(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 0, 1, 2, 3, 4 }), "Equals #1");
            Assert.IsTrue(Collection.Equals(new byte[] { 0, 1, 2, 3, 4 }, 1, new byte[] { 0, 0, 0, 0, 1, 2, 3, 4 }, 4, 4), "Equals #2");

            Assert.IsTrue(Collection.Equals(new int[] { 0, 1, 2, 3, 4 }, new int[] { 0, 1, 2, 3, 4 }), "Equals #1");
            Assert.IsTrue(Collection.Equals(new int[] { 0, 1, 2, 3, 4 }, 1, new int[] { 0, 0, 0, 0, 1, 2, 3, 4 }, 4, 4), "Equals #2");

            Assert.IsTrue(Collection.Equals(new byte[] { 0, 1, 2, 3, 4 }, 1, new byte[] { 0, 0, 0, 0, 1, 2, 3, 4 }, 4, 4, new ByteEqualityComparer()), "Equals #1");

            Assert.IsTrue(Collection.Compare(new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 0 }) == 0, "Compare #1");
            Assert.IsTrue(Collection.Compare(new byte[] { 1, 0, 0 }, new byte[] { 0, 0, 0 }) > 0, "Compare #2");
            Assert.IsTrue(Collection.Compare(new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 10 }) < 0, "Compare #3");
            Assert.IsTrue(Collection.Compare(new byte[] { 0, 0, 0, 0, 0 }, new byte[] { 10 }) > 0, "Compare #4");
            Assert.IsTrue(Collection.Compare(new byte[] { 1, 0, 0, 0 }, new byte[] { 0, 0, 10 }) > 0, "Compare #5");
            Assert.IsTrue(Collection.Compare(new byte[] { 0, 0, 0 }, 1, new byte[] { 0, 0, 0 }, 1, 2) == 0, "Compare #6");
            Assert.IsTrue(Collection.Compare(new byte[] { 1, 0, 0 }, 0, new byte[] { 0, 0, 0 }, 0, 1) > 0, "Compare #7");
            Assert.IsTrue(Collection.Compare(new byte[] { 0, 0, 0 }, 2, new byte[] { 0, 0, 10 }, 2, 1) < 0, "Compare #8");
            Assert.IsTrue(Collection.Compare(new byte[] { 0, 0, 0, 0, 0 }, 0, new byte[] { 10 }, 0, 5) > 0, "Compare #9");
            Assert.IsTrue(Collection.Compare(new byte[] { 1, 0, 0, 0 }, 0, new byte[] { 0, 0, 10 }, 0, 3) > 0, "Compare #10");

            Assert.IsTrue(Collection.Compare(new int[] { 0, 0, 0 }, new int[] { 0, 0, 0 }) == 0, "Compare #1");
            Assert.IsTrue(Collection.Compare(new int[] { 1, 0, 0 }, new int[] { 0, 0, 0 }) > 0, "Compare #2");
            Assert.IsTrue(Collection.Compare(new int[] { 0, 0, 0 }, new int[] { 0, 0, 10 }) < 0, "Compare #3");
            Assert.IsTrue(Collection.Compare(new int[] { 0, 0, 0, 0, 0 }, new int[] { 10 }) > 0, "Compare #4");
            Assert.IsTrue(Collection.Compare(new int[] { 1, 0, 0, 0 }, new int[] { 0, 0, 10 }) > 0, "Compare #5");
            Assert.IsTrue(Collection.Compare(new int[] { 0, 0, 0 }, 1, new int[] { 0, 0, 0 }, 1, 2) == 0, "Compare #6");
            Assert.IsTrue(Collection.Compare(new int[] { 1, 0, 0 }, 0, new int[] { 0, 0, 0 }, 0, 1) > 0, "Compare #7");
            Assert.IsTrue(Collection.Compare(new int[] { 0, 0, 0 }, 2, new int[] { 0, 0, 10 }, 2, 1) < 0, "Compare #8");
            Assert.IsTrue(Collection.Compare(new int[] { 0, 0, 0, 0, 0 }, 0, new int[] { 10 }, 0, 5) > 0, "Compare #9");
            Assert.IsTrue(Collection.Compare(new int[] { 1, 0, 0, 0 }, 0, new int[] { 0, 0, 10 }, 0, 3) > 0, "Compare #10");

            Assert.IsTrue(Collection.Compare(new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 0 }, new ByteComparer()) == 0, "Compare #1");
            Assert.IsTrue(Collection.Compare(new byte[] { 1, 0, 0 }, new byte[] { 0, 0, 0 }, new ByteComparer()) > 0, "Compare #2");
            Assert.IsTrue(Collection.Compare(new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 10 }, new ByteComparer()) < 0, "Compare #3");
            Assert.IsTrue(Collection.Compare(new byte[] { 0, 0, 0, 0, 0 }, new byte[] { 10 }, new ByteComparer()) > 0, "Compare #4");
            Assert.IsTrue(Collection.Compare(new byte[] { 1, 0, 0, 0 }, new byte[] { 0, 0, 10 }, new ByteComparer()) > 0, "Compare #5");
            Assert.IsTrue(Collection.Compare(new byte[] { 0, 0, 0 }, 1, new byte[] { 0, 0, 0 }, 1, 2, new ByteComparer()) == 0, "Compare #6");
            Assert.IsTrue(Collection.Compare(new byte[] { 1, 0, 0 }, 0, new byte[] { 0, 0, 0 }, 0, 1, new ByteComparer()) > 0, "Compare #7");
            Assert.IsTrue(Collection.Compare(new byte[] { 0, 0, 0 }, 2, new byte[] { 0, 0, 10 }, 2, 1, new ByteComparer()) < 0, "Compare #8");
            Assert.IsTrue(Collection.Compare(new byte[] { 0, 0, 0, 0, 0 }, 0, new byte[] { 10 }, 0, 10, new ByteComparer()) > 0, "Compare #9");
            Assert.IsTrue(Collection.Compare(new byte[] { 1, 0, 0, 0 }, 0, new byte[] { 0, 0, 10 }, 0, 3, new ByteComparer()) > 0, "Compare #10");
        }

        sealed class ByteEqualityComparer : IEqualityComparer<byte>
        {
            #region IEqualityComparer<byte>

            public bool Equals(byte x, byte y)
            {
                return x.Equals(y);
            }

            public int GetHashCode(byte obj)
            {
                return (int)obj;
            }

            #endregion
        }

        sealed class ByteComparer : IComparer<byte>
        {
            public int Compare(byte x, byte y)
            {
                return x - y;
            }
        }

        [Test]
        public void Test_NetworkConverter()
        {
            Assert.IsTrue(NetworkConverter.ToHexString(new byte[] { 0x00, 0x9e, 0x0f }) == "009e0f", "ToHexString");
            Assert.IsTrue(Collection.Equals(NetworkConverter.FromHexString("51af4b"), new byte[] { 0x51, 0xaf, 0x4b }), "FromHexString");

            Assert.IsTrue(NetworkConverter.ToBoolean(new byte[] { 0x01 }), "ToBoolean");
            Assert.IsTrue(NetworkConverter.ToChar(new byte[] { 0x00, 0x41 }) == 'A', "ToChar");
            Assert.IsTrue(NetworkConverter.ToInt16(new byte[] { 0x74, 0xab }) == 0x74ab, "ToInt16");
            Assert.IsTrue(NetworkConverter.ToUInt16(new byte[] { 0x74, 0xab }) == 0x74ab, "ToUInt16");
            Assert.IsTrue(NetworkConverter.ToInt32(new byte[] { 0x74, 0xab, 0x05, 0xc1 }) == 0x74ab05c1, "ToInt32");
            Assert.IsTrue(NetworkConverter.ToUInt32(new byte[] { 0x74, 0xab, 0x05, 0xc1 }) == 0x74ab05c1, "ToUInt32");
            Assert.IsTrue(NetworkConverter.ToInt64(new byte[] { 0x74, 0xab, 0xa5, 0xbb, 0xf5, 0x39, 0x7b, 0x15 }) == 0x74aba5bbf5397b15, "ToInt64");
            Assert.IsTrue(NetworkConverter.ToUInt64(new byte[] { 0x74, 0xab, 0xa5, 0xbb, 0xf5, 0x39, 0x7b, 0x15 }) == 0x74aba5bbf5397b15, "ToUInt64");
            Assert.IsTrue(NetworkConverter.ToSingle(new byte[] { 0x4a, 0x8a, 0xd0, 0x64 }) == 4548658.0, "ToSingle");
            Assert.IsTrue(NetworkConverter.ToDouble(new byte[] { 0x41, 0xb8, 0xa6, 0xb9, 0x83, 0x27, 0x97, 0xa3 }) == 413579651.15465754, "ToDouble");

            Assert.IsTrue(Collection.Equals(NetworkConverter.GetBytes(true), new byte[] { 0x01 }), "GetBytes #bool");
            Assert.IsTrue(Collection.Equals(NetworkConverter.GetBytes((char)'A'), new byte[] { 0x00, 0x41 }), "GetBytes #char");
            Assert.IsTrue(Collection.Equals(NetworkConverter.GetBytes((short)0x74ab), new byte[] { 0x74, 0xab }), "GetBytes #short");
            Assert.IsTrue(Collection.Equals(NetworkConverter.GetBytes((ushort)0x74ab), new byte[] { 0x74, 0xab }), "GetBytes #ushort");
            Assert.IsTrue(Collection.Equals(NetworkConverter.GetBytes((int)0x74ab05c1), new byte[] { 0x74, 0xab, 0x05, 0xc1 }), "GetBytes #int");
            Assert.IsTrue(Collection.Equals(NetworkConverter.GetBytes((uint)0x74ab05c1), new byte[] { 0x74, 0xab, 0x05, 0xc1 }), "GetBytes #uint");
            Assert.IsTrue(Collection.Equals(NetworkConverter.GetBytes((long)0x74aba5bbf5397b15), new byte[] { 0x74, 0xab, 0xa5, 0xbb, 0xf5, 0x39, 0x7b, 0x15 }), "GetBytes #long");
            Assert.IsTrue(Collection.Equals(NetworkConverter.GetBytes((ulong)0x74aba5bbf5397b15), new byte[] { 0x74, 0xab, 0xa5, 0xbb, 0xf5, 0x39, 0x7b, 0x15 }), "GetBytes #ulong");
            Assert.IsTrue(Collection.Equals(NetworkConverter.GetBytes((float)4548658.0), new byte[] { 0x4a, 0x8a, 0xd0, 0x64 }), "GetBytes #float");
            Assert.IsTrue(Collection.Equals(NetworkConverter.GetBytes((double)413579651.15465754), new byte[] { 0x41, 0xb8, 0xa6, 0xb9, 0x83, 0x27, 0x97, 0xa3 }), "GetBytes #double");
        }

        [Test]
        public void Test_BufferManager()
        {
            using (BufferManager manager = new BufferManager())
            {
                byte[] buffer1 = manager.TakeBuffer(1024);
                byte[] buffer2 = manager.TakeBuffer(1024 * 10);

                manager.ReturnBuffer(buffer1);
                manager.ReturnBuffer(buffer2);

                buffer1 = null;
                buffer2 = null;

                buffer1 = manager.TakeBuffer(1024);
                buffer2 = manager.TakeBuffer(1024 * 2);

                manager.ReturnBuffer(buffer1);
                manager.ReturnBuffer(buffer2);
            }
        }
    }
}

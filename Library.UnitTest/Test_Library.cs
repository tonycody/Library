using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library")]
    public class Test_Library
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        [Test]
        public void Test_Collection()
        {
            {
                Assert.IsTrue(CollectionUtilities.Equals(new byte[] { 0, 1, 2, 3, 4 }, new byte[] { 0, 1, 2, 3, 4 }), "Equals #1");
                Assert.IsTrue(CollectionUtilities.Equals(new byte[] { 0, 1, 2, 3, 4 }, 1, new byte[] { 0, 0, 0, 0, 1, 2, 3, 4 }, 4, 4), "Equals #2");
                Assert.IsTrue(CollectionUtilities.Equals(new int[] { 0, 1, 2, 3, 4 }, new int[] { 0, 1, 2, 3, 4 }), "Equals #1");
                Assert.IsTrue(CollectionUtilities.Equals(new int[] { 0, 1, 2, 3, 4 }, 1, new int[] { 0, 0, 0, 0, 1, 2, 3, 4 }, 4, 4), "Equals #2");
                Assert.IsTrue(CollectionUtilities.Equals(new byte[] { 0, 1, 2, 3, 4 }, 1, new byte[] { 0, 0, 0, 0, 1, 2, 3, 4 }, 4, 4, new ByteEqualityComparer()), "Equals #1");
            }

            {
                Assert.IsTrue(CollectionUtilities.Equals((IEnumerable<byte>)new byte[] { 0, 1, 2, 3, 4 }, (IEnumerable<byte>)new byte[] { 0, 1, 2, 3, 4 }), "Equals #1");
                Assert.IsTrue(CollectionUtilities.Equals((IEnumerable<byte>)new byte[] { 0, 1, 2, 3, 4 }, 1, (IEnumerable<byte>)new byte[] { 0, 0, 0, 0, 1, 2, 3, 4 }, 4, 4), "Equals #2");
                Assert.IsTrue(CollectionUtilities.Equals((IEnumerable<int>)new int[] { 0, 1, 2, 3, 4 }, (IEnumerable<int>)new int[] { 0, 1, 2, 3, 4 }), "Equals #1");
                Assert.IsTrue(CollectionUtilities.Equals((IEnumerable<int>)new int[] { 0, 1, 2, 3, 4 }, 1, (IEnumerable<int>)new int[] { 0, 0, 0, 0, 1, 2, 3, 4 }, 4, 4), "Equals #2");
                Assert.IsTrue(CollectionUtilities.Equals((IEnumerable<byte>)new byte[] { 0, 1, 2, 3, 4 }, 1, (IEnumerable<byte>)new byte[] { 0, 0, 0, 0, 1, 2, 3, 4 }, 4, 4, new ByteEqualityComparer()), "Equals #1");
            }

            {
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 0 }) == 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 1, 0, 0 }, new byte[] { 0, 0, 0 }) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 10 }) < 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 0, 0, 0, 0, 0 }, new byte[] { 10 }) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 1, 0, 0, 0 }, new byte[] { 0, 0, 0, 10 }) > 0, "Compare");

                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 0, 0, 0 }, 1, new byte[] { 0, 0, 0 }, 1, 2) == 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 1, 0, 0 }, 0, new byte[] { 0, 0, 0 }, 0, 1) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 0, 0, 0 }, 2, new byte[] { 0, 0, 10 }, 2, 1) < 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 0, 0, 0, 0, 0 }, 0, new byte[] { 10 }, 0, 1) < 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 1, 0, 0, 0 }, 0, new byte[] { 0, 0, 10 }, 0, 3) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 1, 0, 0, 0 }, 0, new byte[] { 0, 0, 0, 10 }, 0, 4) > 0, "Compare");

                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 0 }, new ByteComparer()) == 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 1, 0, 0 }, new byte[] { 0, 0, 0 }, new ByteComparer()) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 10 }, new ByteComparer()) < 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 0, 0, 0, 0, 0 }, new byte[] { 10 }, new ByteComparer()) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 1, 0, 0, 0 }, new byte[] { 0, 0, 0, 10 }, new ByteComparer()) > 0, "Compare");

                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 0, 0, 0 }, 1, new byte[] { 0, 0, 0 }, 1, 2, new ByteComparer()) == 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 1, 0, 0 }, 0, new byte[] { 0, 0, 0 }, 0, 1, new ByteComparer()) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 0, 0, 0 }, 2, new byte[] { 0, 0, 10 }, 2, 1, new ByteComparer()) < 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 0, 0, 0, 0, 0 }, 0, new byte[] { 10 }, 0, 1, new ByteComparer()) < 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 1, 0, 0, 0 }, 0, new byte[] { 0, 0, 10 }, 0, 3, new ByteComparer()) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare(new byte[] { 1, 0, 0, 0 }, 0, new byte[] { 0, 0, 0, 10 }, 0, 4, new ByteComparer()) > 0, "Compare");
            }

            {
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 0 }) == 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 1, 0, 0 }, new byte[] { 0, 0, 0 }) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 10 }) < 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 0, 0, 0, 0, 0 }, new byte[] { 10 }) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 1, 0, 0, 0 }, new byte[] { 0, 0, 0, 10 }) > 0, "Compare");

                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 0, 0, 0 }, 1, new byte[] { 0, 0, 0 }, 1, 2) == 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 1, 0, 0 }, 0, new byte[] { 0, 0, 0 }, 0, 1) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 0, 0, 0 }, 2, new byte[] { 0, 0, 10 }, 2, 1) < 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 0, 0, 0, 0, 0 }, 0, new byte[] { 10 }, 0, 1) < 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 1, 0, 0, 0 }, 0, new byte[] { 0, 0, 10 }, 0, 3) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 1, 0, 0, 0 }, 0, new byte[] { 0, 0, 0, 10 }, 0, 4) > 0, "Compare");

                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 0 }, new ByteComparer()) == 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 1, 0, 0 }, new byte[] { 0, 0, 0 }, new ByteComparer()) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 10 }, new ByteComparer()) < 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 0, 0, 0, 0, 0 }, new byte[] { 10 }, new ByteComparer()) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 1, 0, 0, 0 }, new byte[] { 0, 0, 0, 10 }, new ByteComparer()) > 0, "Compare");

                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 0, 0, 0 }, 1, new byte[] { 0, 0, 0 }, 1, 2, new ByteComparer()) == 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 1, 0, 0 }, 0, new byte[] { 0, 0, 0 }, 0, 1, new ByteComparer()) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 0, 0, 0 }, 2, new byte[] { 0, 0, 10 }, 2, 1, new ByteComparer()) < 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 0, 0, 0, 0, 0 }, 0, new byte[] { 10 }, 0, 1, new ByteComparer()) < 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 1, 0, 0, 0 }, 0, new byte[] { 0, 0, 10 }, 0, 3, new ByteComparer()) > 0, "Compare");
                Assert.IsTrue(CollectionUtilities.Compare((IEnumerable<byte>)new byte[] { 1, 0, 0, 0 }, 0, new byte[] { 0, 0, 0, 10 }, 0, 4, new ByteComparer()) > 0, "Compare");
            }
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
            Assert.IsTrue(CollectionUtilities.Equals(NetworkConverter.FromHexString("1af4b"), new byte[] { 0x01, 0xaf, 0x4b }), "FromHexString");

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

            Assert.IsTrue(CollectionUtilities.Equals(NetworkConverter.GetBytes(true), new byte[] { 0x01 }), "GetBytes #bool");
            Assert.IsTrue(CollectionUtilities.Equals(NetworkConverter.GetBytes((char)'A'), new byte[] { 0x00, 0x41 }), "GetBytes #char");
            Assert.IsTrue(CollectionUtilities.Equals(NetworkConverter.GetBytes((short)0x74ab), new byte[] { 0x74, 0xab }), "GetBytes #short");
            Assert.IsTrue(CollectionUtilities.Equals(NetworkConverter.GetBytes((ushort)0x74ab), new byte[] { 0x74, 0xab }), "GetBytes #ushort");
            Assert.IsTrue(CollectionUtilities.Equals(NetworkConverter.GetBytes((int)0x74ab05c1), new byte[] { 0x74, 0xab, 0x05, 0xc1 }), "GetBytes #int");
            Assert.IsTrue(CollectionUtilities.Equals(NetworkConverter.GetBytes((uint)0x74ab05c1), new byte[] { 0x74, 0xab, 0x05, 0xc1 }), "GetBytes #uint");
            Assert.IsTrue(CollectionUtilities.Equals(NetworkConverter.GetBytes((long)0x74aba5bbf5397b15), new byte[] { 0x74, 0xab, 0xa5, 0xbb, 0xf5, 0x39, 0x7b, 0x15 }), "GetBytes #long");
            Assert.IsTrue(CollectionUtilities.Equals(NetworkConverter.GetBytes((ulong)0x74aba5bbf5397b15), new byte[] { 0x74, 0xab, 0xa5, 0xbb, 0xf5, 0x39, 0x7b, 0x15 }), "GetBytes #ulong");
            Assert.IsTrue(CollectionUtilities.Equals(NetworkConverter.GetBytes((float)4548658.0), new byte[] { 0x4a, 0x8a, 0xd0, 0x64 }), "GetBytes #float");
            Assert.IsTrue(CollectionUtilities.Equals(NetworkConverter.GetBytes((double)413579651.15465754), new byte[] { 0x41, 0xb8, 0xa6, 0xb9, 0x83, 0x27, 0x97, 0xa3 }), "GetBytes #double");

            Assert.IsTrue(NetworkConverter.ToInt32(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x74, 0xab, 0x05, 0xc1 }, 4) == 0x74ab05c1, "ToInt32");

            for (int i = 0; i < 1024; i++)
            {
                byte[] buffer = new byte[_random.Next(0, 128)];
                _random.NextBytes(buffer);

                var s = NetworkConverter.ToBase64UrlString(buffer);
                Assert.IsTrue(CollectionUtilities.Equals(buffer, NetworkConverter.FromBase64UrlString(s)));
            }

            for (int i = 0; i < 1024; i++)
            {
                byte[] buffer = new byte[_random.Next(0, 128)];
                _random.NextBytes(buffer);

                var s = NetworkConverter.ToHexString(buffer);
                Assert.IsTrue(CollectionUtilities.Equals(buffer, NetworkConverter.FromHexString(s)));
            }
        }

        [Test]
        public void Test_BufferManager()
        {
            byte[] buffer1 = _bufferManager.TakeBuffer(1024);
            byte[] buffer2 = _bufferManager.TakeBuffer(1024 * 10);

            Assert.IsTrue(buffer1.Length >= 1024, "BufferManager #1");
            Assert.IsTrue(buffer2.Length >= 1024 * 10, "BufferManager #2");

            _bufferManager.ReturnBuffer(buffer1);
            _bufferManager.ReturnBuffer(buffer2);
        }

        [Test]
        public void Test_InternPool()
        {
            //var w = new WeakReference(new object());
            InternPool<object> rt = new InternPool<object>();

            {
                var target1 = rt.GetValue(new Uri("http://127.0.0.1/"), new object()); // Set
                var target2 = rt.GetValue(new Uri("http://127.0.0.1/"), new object());

                Assert.IsTrue(object.ReferenceEquals(target1, target2));
            }

            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();

            rt.Refresh();
            Assert.IsTrue(rt.Count == 0);
        }

        [Test]
        public void Test_Xor()
        {
            {
                byte[] value1 = new byte[1024];
                byte[] value2 = new byte[1024];

                byte[] result1 = new byte[1024];
                byte[] result2 = new byte[1024];

                for (int i = 0; i < 1024; i++)
                {
                    _random.NextBytes(value1);
                    _random.NextBytes(value2);

                    Native.Xor(value1, value2, result1);
                    Native.Xor(value1, 0, value2, 0, result2, 0, result2.Length);

                    Assert.IsTrue(Native.Equals(result1, result2));
                }
            }

            {
                byte[] value1 = new byte[] { 0x00, 0x01, 0x02, };
                byte[] value2 = new byte[] { 0x00 };

                byte[] result1 = new byte[1024];
                _random.NextBytes(result1);
                byte[] result2 = new byte[1024];

                Native.Xor(value1, value2, result1);
                Native.Copy(value1, 0, result2, 0, value1.Length);

                Assert.IsTrue(Native.Equals(result1, result2));
            }

            {
                byte[] value1 = new byte[] { 0x00 };
                byte[] value2 = new byte[] { 0x00, 0x01, 0x02, };

                byte[] result1 = new byte[1024];
                _random.NextBytes(result1);
                byte[] result2 = new byte[1024];

                Native.Xor(value1, value2, result1);
                Native.Copy(value2, 0, result2, 0, value2.Length);

                Assert.IsTrue(Native.Equals(result1, result2));
            }

            {
                byte[] value1 = new byte[] { 0x00, 0x01, 0x00, 0x03 };
                byte[] value2 = new byte[] { 0x00, 0x00, 0x02, 0x00, 0x04 };

                byte[] result1 = new byte[3];
                _random.NextBytes(result1);
                byte[] result2 = new byte[] { 0x00, 0x01, 0x02 };

                Native.Xor(value1, value2, result1);

                Assert.IsTrue(Native.Equals(result1, result2));
            }
        }
    }
}

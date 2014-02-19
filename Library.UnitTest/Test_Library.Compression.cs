using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Library.Compression;
using Library.Correction;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Compression")]
    public class Test_Library_Compression
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        [Test]
        public void Test_Xz()
        {
            using (MemoryStream stream1 = new MemoryStream())
            using (FileStream stream2 = new FileStream("ssss.xz", FileMode.Create))
            //using (MemoryStream stream2 = new MemoryStream())
            using (MemoryStream stream3 = new MemoryStream())
            {
                for (int i = 0; i < 4; i++)
                {
                    byte[] buffer = new byte[1024 * 1024];
                    _random.NextBytes(buffer);
                    stream1.Write(buffer, 0, buffer.Length);
                }

                stream1.Seek(0, SeekOrigin.Begin);
                Xz.Compress(stream1, stream2, _bufferManager);

                stream2.Seek(0, SeekOrigin.Begin);
                Xz.Decompress(stream2, stream3, _bufferManager);

                stream1.Seek(0, SeekOrigin.Begin);
                stream3.Seek(0, SeekOrigin.Begin);

                Assert.AreEqual(stream1.Length, stream3.Length);

                for (; ; )
                {
                    byte[] buffer1 = new byte[1024 * 32];
                    int buffer1Length;
                    byte[] buffer2 = new byte[1024 * 32];
                    int buffer2Length;

                    if ((buffer1Length = stream1.Read(buffer1, 0, buffer1.Length)) <= 0) break;
                    if ((buffer2Length = stream3.Read(buffer2, 0, buffer2.Length)) <= 0) break;

                    Assert.IsTrue(Collection.Equals(buffer1, 0, buffer2, 0, buffer1Length));
                }
            }
        }
    }
}

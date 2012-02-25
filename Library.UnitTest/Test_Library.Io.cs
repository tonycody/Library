using System;
using System.IO;
using Library.Io;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Io")]
    public class Test_Library_Io
    {
        [Test]
        public void Test_BufferStream()
        {
            using (BufferManager manager = new BufferManager())
            {
                Random rand = new Random();

                for (int i = 0; i < 10; i++)
                {
                    ////using (MemoryStream stream = new MemoryStream())
                    using (BufferStream stream = new BufferStream(manager))
                    {
                        byte[] buffer = manager.TakeBuffer(rand.Next(128, 1024 * 10)); ////new byte[rand.Next(128, 1024 * 1024 * 10)];
                        long seek = rand.Next(64, buffer.Length);

                        rand.NextBytes(buffer);

                        stream.Write(buffer, 0, buffer.Length);
                        stream.Position = seek;

                        byte[] buff2 = manager.TakeBuffer(buffer.Length); ////new byte[buffer.Length];
                        stream.Read(buff2, (int)seek, buff2.Length - (int)seek);

                        if (!Collection.Equals(buffer, (int)seek, buff2, (int)seek, buffer.Length - (int)seek))
                        {
                            Assert.IsTrue(Collection.Equals(buffer, (int)seek, buff2, (int)seek, buffer.Length - (int)seek));
                        }

                        manager.ReturnBuffer(buffer);
                        manager.ReturnBuffer(buff2);
                    }
                }
            }
        }

        [Test]
        public void Test_RangeStream()
        {
            using (MemoryStream memoryStream = new MemoryStream(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }))
            {
                byte[] mbyte = new byte[4];
                memoryStream.Seek(7, SeekOrigin.Begin);
                memoryStream.Read(mbyte, 0, mbyte.Length);
                memoryStream.Seek(5, SeekOrigin.Begin);

                using (RangeStream rangeStream = new RangeStream(memoryStream, 7, 4))
                {
                    Assert.AreEqual(memoryStream.Position, 7, "RangeStream #1");
                    Assert.AreEqual(rangeStream.Position, 0, "RangeStream #2");

                    byte[] rbyte = new byte[4];
                    rangeStream.Read(rbyte, 0, rbyte.Length);

                    Assert.AreEqual(rangeStream.Length, 4, "RangeStream #3");
                    Assert.IsTrue(Collection.Equals(mbyte, rbyte), "RangeStream #4");
                }
            }
        }

        [Test]
        public void Test_AddStream()
        {
            using (MemoryStream memoryStream1 = new MemoryStream(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }))
            using (MemoryStream memoryStream2 = new MemoryStream(new byte[] { 8, 9, 10, 11, 12, 13, 14 }))
            {
                using (AddStream addStream = new AddStream(new RangeStream(memoryStream1, 2, 4), new RangeStream(memoryStream2, 2, 4)))
                {
                    byte[] buffer1 = new byte[2];
                    addStream.Read(buffer1, 0, buffer1.Length);
                    Assert.IsTrue(Collection.Equals(new byte[] { 2, 3 }, buffer1), "AddStream #1");

                    byte[] buffer2 = new byte[2];
                    addStream.Read(buffer2, 0, buffer2.Length);
                    Assert.IsTrue(Collection.Equals(new byte[] { 4, 5 }, buffer2), "AddStream #2");

                    byte[] buffer3 = new byte[2];
                    addStream.Read(buffer3, 0, buffer3.Length);
                    Assert.IsTrue(Collection.Equals(new byte[] { 10, 11 }, buffer3), "AddStream #3");

                    byte[] buffer4 = new byte[2];
                    addStream.Read(buffer4, 0, buffer4.Length);
                    Assert.IsTrue(Collection.Equals(new byte[] { 12, 13 }, buffer4), "AddStream #4");
                }
            }
        }

        [Test]
        public void Test_ProgressStream()
        {
            int count = 0;

            try
            {
                using (MemoryStream memoryStream = new MemoryStream(new byte[4096 * 10]))
                using (ProgressStream progressStream = new ProgressStream(
                    memoryStream,
                    (object sender, long readSize, long writeSize, out bool isStop) =>
                    {
                        count++;

                        if (readSize >= 8192)
                        {
                            isStop = true;
                        }
                        else
                        {
                            isStop = false;
                        }
                    },
                    4096))
                {
                    byte[] tbyte = new byte[4096];

                    for (int i = 0; i < 4; i++)
                    {
                        progressStream.Read(tbyte, 0, tbyte.Length);
                    }
                }
            }
            catch (StopIOException)
            {
                Assert.AreEqual(count, 2, "ProgressStream");
            }
        }
    }
}

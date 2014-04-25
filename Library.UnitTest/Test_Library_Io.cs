using System;
using System.IO;
using System.Threading.Tasks;
using Library.Io;
using Library.Security;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Io")]
    public class Test_Library_Io
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        [Test]
        public void Test_BufferStream()
        {
            //for (int i = 0; i < 10; i++)
            Parallel.For(0, 32, new ParallelOptions() { MaxDegreeOfParallelism = 64 }, i =>
            {
                ////using (MemoryStream stream = new MemoryStream())
                using (BufferStream stream = new BufferStream(_bufferManager))
                {
                    byte[] buffer = _bufferManager.TakeBuffer(1024 * 1024); ////new byte[_random.Next(128, 1024 * 1024 * 10)];
                    long seek = _random.Next(64, buffer.Length);
                    //long seek = 0;

                    _random.NextBytes(buffer);

                    stream.Write(buffer, 0, buffer.Length);
                    stream.Position = seek;

                    byte[] buff2 = _bufferManager.TakeBuffer(buffer.Length); ////new byte[buffer.Length];
                    stream.Read(buff2, (int)seek, buff2.Length - (int)seek);

                    if (!CollectionUtilities.Equals(buffer, (int)seek, buff2, (int)seek, buffer.Length - (int)seek))
                    {
                        Assert.IsTrue(CollectionUtilities.Equals(buffer, (int)seek, buff2, (int)seek, buffer.Length - (int)seek));
                    }

                    _bufferManager.ReturnBuffer(buffer);
                    _bufferManager.ReturnBuffer(buff2);
                }
            });

            using (MemoryStream mstream = new MemoryStream())
            using (BufferStream stream = new BufferStream(_bufferManager))
            {
                for (int i = 0; i < 1024 * 1024; i++)
                {
                    var v = (byte)_random.Next(0, 255);
                    mstream.WriteByte(v);
                    stream.WriteByte(v);
                }

                mstream.Seek(0, SeekOrigin.Begin);
                stream.Seek(0, SeekOrigin.Begin);

                Assert.IsTrue(mstream.Length == stream.Length);

                for (int i = 0; i < 1024 * 1024; i++)
                {
                    Assert.IsTrue(mstream.ReadByte() == stream.ReadByte());
                }
            }
        }

        [Test]
        public void Test_CacheStream()
        {
            //for (int i = 0; i < 10; i++)
            Parallel.For(0, 32, new ParallelOptions() { MaxDegreeOfParallelism = 64 }, i =>
            {
                ////using (MemoryStream stream = new MemoryStream())
                using (BufferStream bufferStream = new BufferStream(_bufferManager))
                using (CacheStream stream = new CacheStream(bufferStream, 1024, _bufferManager))
                {
                    byte[] buffer = _bufferManager.TakeBuffer(1024 * 1024); ////new byte[_random.Next(128, 1024 * 1024 * 10)];
                    long seek = _random.Next(64, buffer.Length);
                    //long seek = 0;

                    _random.NextBytes(buffer);

                    stream.Write(buffer, 0, buffer.Length);
                    stream.Position = seek;

                    byte[] buff2 = _bufferManager.TakeBuffer(buffer.Length); ////new byte[buffer.Length];
                    stream.Read(buff2, (int)seek, buff2.Length - (int)seek);

                    if (!CollectionUtilities.Equals(buffer, (int)seek, buff2, (int)seek, buffer.Length - (int)seek))
                    {
                        Assert.IsTrue(CollectionUtilities.Equals(buffer, (int)seek, buff2, (int)seek, buffer.Length - (int)seek));
                    }

                    _bufferManager.ReturnBuffer(buffer);
                    _bufferManager.ReturnBuffer(buff2);
                }
            });

            using (MemoryStream mstream = new MemoryStream())
            using (BufferStream bufferStream = new BufferStream(_bufferManager))
            using (CacheStream stream = new CacheStream(bufferStream, 1024, _bufferManager))
            {
                for (int i = 0; i < 1024 * 1024; i++)
                {
                    var v = (byte)_random.Next(0, 255);
                    mstream.WriteByte(v);
                    stream.WriteByte(v);
                }

                mstream.Seek(0, SeekOrigin.Begin);
                stream.Seek(0, SeekOrigin.Begin);

                Assert.IsTrue(mstream.Length == stream.Length);

                for (int i = 0; i < 1024 * 1024; i++)
                {
                    Assert.IsTrue(mstream.ReadByte() == stream.ReadByte());
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
                    Assert.IsTrue(CollectionUtilities.Equals(mbyte, rbyte), "RangeStream #4");
                }
            }
        }

        [Test]
        public void Test_UniteStream()
        {
            using (MemoryStream memoryStream1 = new MemoryStream(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }))
            using (MemoryStream memoryStream2 = new MemoryStream(new byte[] { 8, 9, 10, 11, 12, 13, 14 }))
            {
                using (UniteStream addStream = new UniteStream(new RangeStream(memoryStream1, 2, 4), new RangeStream(memoryStream2, 2, 4)))
                {
                    byte[] buffer1 = new byte[2];
                    addStream.Read(buffer1, 0, buffer1.Length);
                    Assert.IsTrue(CollectionUtilities.Equals(new byte[] { 2, 3 }, buffer1), "UniteStream #1");

                    byte[] buffer2 = new byte[2];
                    addStream.Read(buffer2, 0, buffer2.Length);
                    Assert.IsTrue(CollectionUtilities.Equals(new byte[] { 4, 5 }, buffer2), "UniteStream #2");

                    byte[] buffer3 = new byte[2];
                    addStream.Read(buffer3, 0, buffer3.Length);
                    Assert.IsTrue(CollectionUtilities.Equals(new byte[] { 10, 11 }, buffer3), "UniteStream #3");

                    byte[] buffer4 = new byte[2];
                    addStream.Read(buffer4, 0, buffer4.Length);
                    Assert.IsTrue(CollectionUtilities.Equals(new byte[] { 12, 13 }, buffer4), "UniteStream #4");
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
            catch (StopIoException)
            {
                Assert.AreEqual(count, 2, "ProgressStream");
            }
        }

        [Test]
        public void Test_QueueStream()
        {
            using (FileStream stream1 = new FileStream("QueueStream1.tmp", FileMode.Create))
            using (QueueStream queueStream = new QueueStream(stream1, StreamMode.Write, 1024 * 1024 * 4, _bufferManager))
            using (FileStream stream2 = new FileStream("QueueStream2.tmp", FileMode.Create))
            {
                byte[] buffer = new byte[1024];

                for (int i = 0; i < 1024 * 32; i++)
                {
                    _random.NextBytes(buffer);

                    queueStream.Write(buffer, 0, buffer.Length);
                    stream2.Write(buffer, 0, buffer.Length);
                }
            }

            using (FileStream stream1 = new FileStream("QueueStream1.tmp", FileMode.Open))
            using (FileStream stream2 = new FileStream("QueueStream2.tmp", FileMode.Open))
            {
                Assert.IsTrue(CollectionUtilities.Equals(Sha512.ComputeHash(stream1), Sha512.ComputeHash(stream2)), "QueueStream #1");
            }

            using (FileStream stream1 = new FileStream("QueueStream1.tmp", FileMode.Open))
            using (QueueStream queueStream = new QueueStream(stream1, StreamMode.Read, 1024 * 1024 * 4, _bufferManager))
            using (FileStream stream2 = new FileStream("QueueStream2.tmp", FileMode.Open))
            {
                Assert.IsTrue(CollectionUtilities.Equals(Sha512.ComputeHash(queueStream), Sha512.ComputeHash(stream2)), "QueueStream #2");
            }
        }
    }
}

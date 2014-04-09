using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Library.Correction;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Correction")]
    public class Test_Library_Correction
    {
        private BufferManager _bufferManager = BufferManager.Instance;
        private Random _random = new Random();

        [Test]
        public void Test_ReedSolomon8()
        {
            foreach (var nativeFlag in new bool[] { false, true })
            {
                {
                    ReedSolomon8 pc = new ReedSolomon8(128, 256, nativeFlag);

                    byte[][] buffList = new byte[128][];
                    for (int i = 0; i < 128; i++)
                    {
                        var buffer = new byte[1024 * 32];
                        _random.NextBytes(buffer);

                        buffList[i] = buffer;
                    }

                    byte[][] buffList2 = new byte[256][];
                    for (int i = 0; i < 256; i++)
                    {
                        var buffer = new byte[1024 * 32];

                        buffList2[i] = buffer;
                    }

                    int[] intList = new int[256];
                    for (int i = 0; i < 256; i++)
                    {
                        intList[i] = i;
                    }

                    pc.Encode(buffList, buffList2, intList, 1024 * 32);

                    byte[][] buffList3 = new byte[128][];
                    int buffList3Count = 0;

                    for (int i = 0; i < 64; i++)
                    {
                        buffList3[buffList3Count++] = buffList2[i];
                    }

                    for (int i = 0; i < 64; i++)
                    {
                        buffList3[buffList3Count++] = buffList2[128 + i];
                    }

                    int[] intList2 = new int[128];
                    int intList2Count = 0;

                    for (int i = 0; i < 64; i++)
                    {
                        intList2[intList2Count++] = i;
                    }

                    for (int i = 0; i < 64; i++)
                    {
                        intList2[intList2Count++] = 128 + i;
                    }

                    {
                        int n = buffList3.Length;

                        while (n > 1)
                        {
                            int k = _random.Next(n--);

                            var temp = buffList3[n];
                            buffList3[n] = buffList3[k];
                            buffList3[k] = temp;

                            var temp2 = intList2[n];
                            intList2[n] = intList2[k];
                            intList2[k] = temp2;
                        }
                    }

                    pc.Decode(buffList3, intList2, 1024 * 32);

                    for (int i = 0; i < buffList.Length; i++)
                    {
                        Assert.IsTrue(Collection.Equals(buffList[i], 0, buffList3[i], 0, 1024 * 32), "ReedSolomon");
                    }
                }

                {
                    ReedSolomon8 pc = new ReedSolomon8(128, 256, nativeFlag);

                    byte[][] buffList = new byte[128][];
                    for (int i = 0; i < 128; i++)
                    {
                        var buffer = new byte[1024 * 32];
                        _random.NextBytes(buffer);

                        buffList[i] = buffer;
                    }

                    byte[][] buffList2 = new byte[128][];
                    for (int i = 0; i < 128; i++)
                    {
                        var buffer = new byte[1024 * 32];

                        buffList2[i] = buffer;
                    }

                    int[] intList = new int[128];
                    for (int i = 0; i < 128; i++)
                    {
                        intList[i] = i + 128;
                    }

                    {
                        Stopwatch sw = new Stopwatch();
                        sw.Start();

                        var task1 = Task.Factory.StartNew(() =>
                        {
                            pc.Encode(buffList.ToArray(), buffList2.ToArray(), intList.ToArray(), 1024 * 32);
                        });

                        Thread.Sleep(1000 * 1);

                        var task2 = Task.Factory.StartNew(() =>
                        {
                            pc.Cancel();
                        });

                        Task.WaitAll(task1, task2);

                        sw.Stop();

                        Assert.IsTrue(sw.Elapsed.TotalSeconds < 3);
                    }
                }

                {
                    ReedSolomon8 pc = new ReedSolomon8(128, 256, nativeFlag);

                    byte[][] buffList = new byte[128][];
                    for (int i = 0; i < 128; i++)
                    {
                        var buffer = new byte[1024 * 32];
                        _random.NextBytes(buffer);

                        buffList[i] = buffer;
                    }

                    byte[][] buffList2 = new byte[128][];
                    for (int i = 0; i < 128; i++)
                    {
                        var buffer = new byte[1024 * 32];

                        buffList2[i] = buffer;
                    }

                    int[] intList = new int[128];
                    for (int i = 0; i < 128; i++)
                    {
                        intList[i] = i + 128;
                    }

                    pc.Encode(buffList.ToArray(), buffList2.ToArray(), intList.ToArray(), 1024 * 32);

                    {
                        Stopwatch sw = new Stopwatch();
                        sw.Start();

                        var task1 = Task.Factory.StartNew(() =>
                        {
                            pc.Decode(buffList2.ToArray(), intList.ToArray(), 1024 * 32);
                        });

                        Thread.Sleep(1000 * 1);

                        var task2 = Task.Factory.StartNew(() =>
                        {
                            pc.Cancel();
                        });

                        Task.WaitAll(task1, task2);

                        sw.Stop();

                        Assert.IsTrue(sw.Elapsed.TotalSeconds < 3);
                    }
                }
            }
        }

        [Test]
        public void Test_ReedSolomon()
        {
            {
                ReedSolomon pc = new ReedSolomon(8, 128, 256, 4, _bufferManager);

                var buffList = new ArraySegment<byte>[128];
                for (int i = 0; i < 128; i++)
                {
                    var buffer = new byte[1024 * 32];
                    _random.NextBytes(buffer);

                    buffList[i] = new ArraySegment<byte>(buffer, 0, buffer.Length);
                }

                var buffList2 = new ArraySegment<byte>[256];
                for (int i = 0; i < 256; i++)
                {
                    var buffer = new byte[1024 * 32];

                    buffList2[i] = new ArraySegment<byte>(buffer, 0, buffer.Length);
                }

                var intList = new int[256];
                for (int i = 0; i < 256; i++)
                {
                    intList[i] = i;
                }

                pc.Encode(buffList, buffList2, intList, 1024 * 32);

                var buffList3 = new ArraySegment<byte>[128];
                {
                    int i = 0;

                    for (int j = 0; i < 64; i++, j++)
                    {
                        buffList3[i] = buffList2[i];
                    }
                    for (int j = 0; i < 128; i++, j++)
                    {
                        buffList3[i] = buffList2[128 + j];
                    }
                }

                var intList2 = new int[128];
                {
                    int i = 0;

                    for (int j = 0; i < 64; i++, j++)
                    {
                        intList2[i] = i;
                    }
                    for (int j = 0; i < 128; i++, j++)
                    {
                        intList2[i] = 128 + j;
                    }
                }

                {
                    int n = buffList3.Length;

                    while (n > 1)
                    {
                        int k = _random.Next(n--);

                        var temp = buffList3[n];
                        buffList3[n] = buffList3[k];
                        buffList3[k] = temp;

                        var temp2 = intList2[n];
                        intList2[n] = intList2[k];
                        intList2[k] = temp2;
                    }
                }

                // これだと(参照が)ToArrayで切り離され、Decode内部からIListをシャッフルしている意味が無くなるため、正常にデコードできない
                //pc.Decode(buffList3.ToArray(), intList2.ToArray());          

                pc.Decode(buffList3, intList2, 1024 * 32);

                for (int i = 0; i < buffList.Length; i++)
                {
                    Assert.IsTrue(Collection.Equals(buffList[i].Array, buffList[i].Offset, buffList3[i].Array, buffList3[i].Offset, 4), "ReedSolomon");
                }
            }

        }
    }
}
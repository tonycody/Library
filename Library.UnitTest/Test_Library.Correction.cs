using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public void Test_ReedSolomon()
        {
            {
                for (int j = 0; j < 3; j++)
                {
                    ReedSolomon pc = new ReedSolomon(8, 128, 256, 1, _bufferManager);

                    IList<ArraySegment<byte>> buffList = new List<ArraySegment<byte>>();
                    for (int i = 0; i < 128; i++)
                    {
                        var buffer = _bufferManager.TakeBuffer(1024 * 32);
                        _random.NextBytes(buffer);

                        buffList.Add(new ArraySegment<byte>(buffer, 0, 1024 * 32));
                    }

                    IList<ArraySegment<byte>> buffList2 = new List<ArraySegment<byte>>();
                    for (int i = 0; i < 256; i++)
                    {
                        var buffer = _bufferManager.TakeBuffer(1024 * 32);
                        Array.Clear(buffer, 0, 1024 * 32);

                        buffList2.Add(new ArraySegment<byte>(buffer, 0, 1024 * 32));
                    }

                    List<int> intList = new List<int>();
                    for (int i = 0; i < 256; i++)
                    {
                        intList.Add(i);
                    }

                    Stopwatch sw = new Stopwatch();
                    sw.Start();

                    pc.Encode(buffList, buffList2, intList.ToArray());

                    sw.Stop();
                    Debug.WriteLine(sw.Elapsed.ToString());

                    foreach (var buffer in buffList)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }

                    foreach (var buffer in buffList2)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }

                Debug.WriteLine("");

                for (int j = 0; j < 3; j++)
                {
                    ReedSolomon pc = new ReedSolomon(8, 128, 256, 4, _bufferManager);

                    IList<ArraySegment<byte>> buffList = new List<ArraySegment<byte>>();
                    for (int i = 0; i < 128; i++)
                    {
                        var buffer = _bufferManager.TakeBuffer(1024 * 32);
                        _random.NextBytes(buffer);

                        buffList.Add(new ArraySegment<byte>(buffer, 0, 1024 * 32));
                    }

                    IList<ArraySegment<byte>> buffList2 = new List<ArraySegment<byte>>();
                    for (int i = 0; i < 256; i++)
                    {
                        var buffer = _bufferManager.TakeBuffer(1024 * 32);
                        Array.Clear(buffer, 0, 1024 * 32);

                        buffList2.Add(new ArraySegment<byte>(buffer, 0, 1024 * 32));
                    }

                    List<int> intList = new List<int>();
                    for (int i = 0; i < 256; i++)
                    {
                        intList.Add(i);
                    }

                    Stopwatch sw = new Stopwatch();
                    sw.Start();

                    pc.Encode(buffList, buffList2, intList.ToArray());

                    sw.Stop();
                    Debug.WriteLine(sw.Elapsed.ToString());

                    foreach (var buffer in buffList)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }

                    foreach (var buffer in buffList2)
                    {
                        _bufferManager.ReturnBuffer(buffer.Array);
                    }
                }
            }

            {
                ReedSolomon pc = new ReedSolomon(8, 128, 256, 1, _bufferManager);

                IList<ArraySegment<byte>> buffList = new List<ArraySegment<byte>>();
                for (int i = 0; i < 128; i++)
                {
                    var buffer = new byte[1024 * 32];
                    _random.NextBytes(buffer);

                    buffList.Add(new ArraySegment<byte>(buffer, 0, buffer.Length));
                }

                IList<ArraySegment<byte>> buffList2 = new List<ArraySegment<byte>>();
                for (int i = 0; i < 256; i++)
                {
                    var buffer = new byte[1024 * 32];

                    buffList2.Add(new ArraySegment<byte>(buffer, 0, buffer.Length));
                }

                List<int> intList = new List<int>();
                for (int i = 0; i < 256; i++)
                {
                    intList.Add(i);
                }

                pc.Encode(buffList, buffList2, intList.ToArray());

                IList<ArraySegment<byte>> buffList3 = new List<ArraySegment<byte>>();

                for (int i = 0; i < 64; i++)
                {
                    buffList3.Add(buffList2[i]);
                }

                for (int i = 0; i < 64; i++)
                {
                    buffList3.Add(buffList2[128 + i]);
                }

                List<int> intList2 = new List<int>();
                for (int i = 0; i < 64; i++)
                {
                    intList2.Add(i);
                }

                for (int i = 0; i < 64; i++)
                {
                    intList2.Add(128 + i);
                }

                {
                    int n = buffList3.Count;

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

                pc.Decode(ref buffList3, intList2.ToArray());

                for (int i = 0; i < buffList.Count; i++)
                {
                    Assert.IsTrue(Collection.Equals(buffList[i].Array, buffList[i].Offset, buffList3[i].Array, buffList3[i].Offset, 4), "ReedSolomon");
                }
            }
        }
    }
}

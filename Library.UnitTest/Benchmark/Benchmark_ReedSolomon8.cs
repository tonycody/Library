using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Library.Correction;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Benchmark")]
    public partial class Benchmark
    {
        [Test]
        public void ReedSolomon8()
        {
            Random random = new Random();

            const int blockSize = 1024 * 1024;

            for (int c = 1; c <= 4; c++)
            {
                ReedSolomon8 pc = new ReedSolomon8(128, 256, c, _bufferManager);

                ArraySegment<byte>[] buffList = new ArraySegment<byte>[128];
                for (int i = 0; i < 128; i++)
                {
                    var buffer = _bufferManager.TakeBuffer(blockSize);
                    random.NextBytes(buffer);

                    buffList[i] = new ArraySegment<byte>(buffer);
                }

                ArraySegment<byte>[] buffList2 = new ArraySegment<byte>[128];
                for (int i = 0; i < 128; i++)
                {
                    var buffer = _bufferManager.TakeBuffer(blockSize);

                    buffList2[i] = new ArraySegment<byte>(buffer);
                }

                int[] intList = new int[128];
                for (int i = 0; i < 128; i++)
                {
                    intList[i] = i + 128;
                }

                Stopwatch sw = new Stopwatch();
                sw.Start();

                pc.Encode(buffList, buffList2, intList, blockSize);
                pc.Decode(buffList2, intList, blockSize);

                sw.Stop();

                for (int i = 0; i < buffList.Length; i++)
                {
                    Assert.IsTrue(CollectionUtilities.Equals(buffList[i].Array, buffList[i].Offset, buffList2[i].Array, buffList2[i].Offset, blockSize), "ReedSolomon");
                }

                for (int i = 0; i < 128; i++)
                {
                    _bufferManager.ReturnBuffer(buffList[i].Array);
                }

                for (int i = 0; i < 128; i++)
                {
                    _bufferManager.ReturnBuffer(buffList2[i].Array);
                }

                Console.WriteLine(string.Format("ReedSolomon8 Parallel {0}: ", c) + sw.Elapsed.ToString());
            }

            Console.Write(Environment.NewLine);
        }
    }
}

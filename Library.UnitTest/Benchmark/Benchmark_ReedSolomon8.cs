using System;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using Library.Correction;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Benchmark")]
    public partial class Benchmark
    {
        private BufferManager _bufferManager = BufferManager.Instance;

        [Test]
        public void ReedSolomon8()
        {
            StringBuilder sb = new StringBuilder();
            Random random = new Random();

            foreach (var nativeFlag in new bool[] { false, true })
            {
                ReedSolomon8 pc = new ReedSolomon8(128, 256, nativeFlag);

                byte[][] buffList = new byte[128][];
                for (int i = 0; i < 128; i++)
                {
                    var buffer = _bufferManager.TakeBuffer(1024 * 128);
                    random.NextBytes(buffer);

                    buffList[i] = buffer;
                }

                byte[][] buffList2 = new byte[128][];
                for (int i = 0; i < 128; i++)
                {
                    var buffer = _bufferManager.TakeBuffer(1024 * 128);

                    buffList2[i] = buffer;
                }

                int[] intList = new int[128];
                for (int i = 0; i < 128; i++)
                {
                    intList[i] = i + 128;
                }

                Stopwatch sw = new Stopwatch();
                sw.Start();

                pc.Encode(buffList, buffList2, intList, 1024 * 128);
                pc.Decode(buffList2, intList, 1024 * 128);

                sw.Stop();

                for (int i = 0; i < 128; i++)
                {
                    _bufferManager.ReturnBuffer(buffList[i]);
                }

                for (int i = 0; i < 128; i++)
                {
                    _bufferManager.ReturnBuffer(buffList2[i]);
                }

                if (nativeFlag)
                {
                    sb.AppendLine("Native ReedSolomon: " + sw.Elapsed.ToString());
                }
                else
                {
                    sb.AppendLine("Managed ReedSolomon: " + sw.Elapsed.ToString());
                }
            }

            MessageBox.Show(sb.ToString());
        }
    }
}

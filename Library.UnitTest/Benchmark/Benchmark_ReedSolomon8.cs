using System;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Library.Net;
using Library.Net.Amoeba;
using NUnit.Framework;
using Library;
using Library.Correction;
using System.Text;

namespace Library.UnitTest
{
    [TestFixture, Category("Benchmark")]
    public partial class Benchmark
    {
        [Test]
        public void ReedSolomon8()
        {
            StringBuilder sb = new StringBuilder();
            Random random = new Random();

            foreach (var nativeFlag in new bool[] { false, true })
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();

                ReedSolomon8 pc = new ReedSolomon8(128, 256, nativeFlag);

                byte[][] buffList = new byte[128][];
                for (int i = 0; i < 128; i++)
                {
                    var buffer = new byte[1024 * 32];
                    random.NextBytes(buffer);

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

                pc.Encode(buffList, buffList2, intList, 1024 * 32);
                pc.Decode(buffList2, intList, 1024 * 32);

                sw.Stop();

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

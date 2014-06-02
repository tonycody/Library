using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Benchmark")]
    public partial class Benchmark
    {
        [Test]
        public void Copy()
        {
            Random random = new Random();

            Stopwatch sw1 = new Stopwatch();
            Stopwatch sw2 = new Stopwatch();

            var flags = new int[] { 0, 1 };

            var length = 1024 * 256;
            byte[] x1 = new byte[length];
            byte[] y1 = new byte[length];
            byte[] x2 = new byte[length];
            byte[] y2 = new byte[length];

            random.NextBytes(x1);
            random.NextBytes(x2);

            for (int i = 0; i < 1024 * 256; i++)
            {
                random.Shuffle(flags);
                foreach (var index in flags)
                {
                    if (index == 0)
                    {
                        sw1.Start();
                        Unsafe.Copy(x1, 0, y1, 0, length);
                        sw1.Stop();
                    }
                    else if (index == 1)
                    {
                        sw2.Start();
                        Array.Copy(x2, 0, y2, 0, length);
                        sw2.Stop();
                    }
                }

                Assert.IsTrue(Unsafe.Equals(x1, y1));
                Assert.IsTrue(Unsafe.Equals(x2, y2));
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Native Copy: " + sw1.Elapsed.ToString());
            sb.AppendLine("Array Copy: " + sw2.Elapsed.ToString());

            Console.WriteLine(sb.ToString());
        }
    }
}

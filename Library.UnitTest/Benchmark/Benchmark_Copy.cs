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

            var length = 1024 * 1024;
            byte[] x = new byte[length];
            byte[] y = new byte[length];

            for (int i = 0; i < 1024 * 32; i++)
            {
                random.Shuffle(flags);
                foreach (var index in flags)
                {
                    if (index == 0)
                    {
                        sw1.Start();
                        Native.Copy(x, 0, y, 0, length);
                        sw1.Stop();
                    }
                    else if (index == 1)
                    {
                        sw2.Start();
                        Array.Copy(x, 0, y, 0, length);
                        sw2.Stop();
                    }
                }
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Native.Copy: " + sw1.Elapsed.ToString());
            sb.AppendLine("Array.Copy: " + sw2.Elapsed.ToString());

            Console.WriteLine(sb.ToString());
        }
    }
}

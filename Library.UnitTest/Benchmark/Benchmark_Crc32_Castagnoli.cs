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
        public void Crc32_Castagnoli()
        {
            Random random = new Random();

            Stopwatch sw1 = new Stopwatch();
            Stopwatch sw2 = new Stopwatch();

            var flags = new int[] { 0, 1 };

            var length = 1024;
            byte[] value = new byte[length];

            for (int i = 0; i < 1024 * 1024; i++)
            {
                byte[] result1 = null;
                byte[] result2 = null;

                random.Shuffle(flags);
                foreach (var index in flags)
                {
                    if (index == 0)
                    {
                        sw1.Start();
                        result1 = Library.Security.Crc32_Castagnoli.ComputeHash(value, 0, length);
                        sw1.Stop();
                    }
                    else if (index == 1)
                    {
                        sw2.Start();
                        result2 = T_Crc32_Castagnoli.ComputeHash(value, 0, length);
                        sw2.Stop();
                    }
                }

                Assert.IsTrue(Unsafe.Equals(result1, result2));
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Native Crc32_Castagnoli: " + sw1.Elapsed.ToString());
            sb.AppendLine("Managed Crc32_Castagnoli: " + sw2.Elapsed.ToString());

            Console.WriteLine(sb.ToString());
        }
    }
}

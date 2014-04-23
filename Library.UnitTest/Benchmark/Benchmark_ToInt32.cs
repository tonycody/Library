using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Library.Security;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Benchmark")]
    public partial class Benchmark
    {
        [Test, Explicit]
        public void ToInt32()
        {
            Random random = new Random();

            Stopwatch sw1 = new Stopwatch();
            Stopwatch sw2 = new Stopwatch();

            var flags = new int[] { 0, 1 };

            for (int i = 0; i < 1024 * 1024 * 32; i++)
            {
                var value = NetworkConverter.GetBytes(random.Next());

                int result1 = 0;
                int result2 = 0;

                random.Shuffle(flags);
                foreach (var index in flags)
                {
                    if (index == 0)
                    {
                        sw1.Start();
                        result1 = NetworkConverter.ToInt32(value, 0);
                        sw1.Stop();
                    }
                    else if (index == 1)
                    {
                        sw2.Start();
                        result2 = NetworkConverter.ToInt32_2(value, 0);
                        sw2.Stop();
                    }
                }

                Assert.IsTrue(result1 == result2);
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Native ToInt32: " + sw1.Elapsed.ToString());
            sb.AppendLine("Native ToInt32_2: " + sw2.Elapsed.ToString());

            Console.WriteLine(sb.ToString());
        }
    }
}

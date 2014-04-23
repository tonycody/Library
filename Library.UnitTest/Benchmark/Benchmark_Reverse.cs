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
        public void Reverse()
        {
            Random random = new Random();

            Stopwatch sw1 = new Stopwatch();
            Stopwatch sw2 = new Stopwatch();

            var flags = new int[] { 0, 1 };

            for (int i = 0; i < 1024 * 1024 * 32; i++)
            {
                var value = NetworkConverter.GetBytes(random.Next());

                byte[] result1 = null;
                byte[] result2 = null;

                random.Shuffle(flags);
                foreach (var index in flags)
                {
                    if (index == 0)
                    {
                        sw1.Start();
                        result1 = NetworkConverter.Reverse(value, 0, 4);
                        sw1.Stop();
                    }
                    else if (index == 1)
                    {
                        sw2.Start();
                        result2 = NetworkConverter.Reverse_2(value, 0, 4);
                        sw2.Stop();
                    }
                }

                Assert.IsTrue(Native.Equals(result1, result2));
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Native Reverse: " + sw1.Elapsed.ToString());
            sb.AppendLine("Native Reverse_2: " + sw2.Elapsed.ToString());

            Console.WriteLine(sb.ToString());
        }
    }
}

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
        [Test]
        public void ToHexString()
        {
            Random random = new Random();

            byte[] value = new byte[32];
            random.NextBytes(value);

            Stopwatch sw1 = new Stopwatch();
            Stopwatch sw2 = new Stopwatch();

            var flags = new int[] { 0, 1 };

            for (int i = 0; i < 1024 * 1024 * 2; i++)
            {
                string result1 = null;
                string result2 = null;

                random.Shuffle(flags);
                foreach (var index in flags)
                {
                    if (index == 0)
                    {
                        sw1.Start();
                        result1 = NetworkConverter.ToHexString(value, 0, value.Length);
                        sw1.Stop();
                    }
                    else if (index == 1)
                    {
                        sw2.Start();
                        result2 = NetworkConverter.ToHexString_2(value, 0, value.Length);
                        sw2.Stop();
                    }
                }

                Assert.IsTrue(result1 == result2);
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("ToHexString: " + sw1.Elapsed.ToString());
            sb.AppendLine("ToHexString_2: " + sw2.Elapsed.ToString());

            Console.WriteLine(sb.ToString());
        }
    }
}

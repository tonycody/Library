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
        public void FromHexString()
        {
            Random random = new Random();

            string value;

            {
                byte[] buffer = new byte[32];
                random.NextBytes(buffer);

                value = NetworkConverter.ToHexString(buffer);
            }

            Stopwatch sw1 = new Stopwatch();
            Stopwatch sw2 = new Stopwatch();

            var flags = new int[] { 0, 1 };

            for (int i = 0; i < 1024 * 1024 * 2; i++)
            {
                byte[] result1 = null;
                byte[] result2 = null;

                random.Shuffle(flags);
                foreach (var index in flags)
                {
                    if (index == 0)
                    {
                        sw1.Start();
                        result1 = NetworkConverter.FromHexString(value);
                        sw1.Stop();
                    }
                    else if (index == 1)
                    {
                        sw2.Start();
                        result2 = NetworkConverter.FromHexString_2(value);
                        sw2.Stop();
                    }
                }

                Assert.IsTrue(Unsafe.Equals(result1, result2));
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("FromHexString: " + sw1.Elapsed.ToString());
            sb.AppendLine("FromHexString_2: " + sw2.Elapsed.ToString());

            Console.WriteLine(sb.ToString());
        }
    }
}

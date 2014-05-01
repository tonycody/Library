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
        public void CheckBase64()
        {
            Random random = new Random();

            var hash = NetworkConverter.ToBase64UrlString(Library.Security.Signature.GetSignatureHash(new DigitalSignature("oooooo", DigitalSignatureAlgorithm.Rsa2048_Sha512).ToString()));
            var hash2 = NetworkConverter.ToBase64UrlString(Library.Security.Signature.GetSignatureHash(new DigitalSignature("oooooo", DigitalSignatureAlgorithm.Rsa2048_Sha512).ToString())) + "ˆŸ";
            //var hash2 = RandomString.GetValue(1024);

            Stopwatch sw1 = new Stopwatch();
            Stopwatch sw2 = new Stopwatch();

            var flags = new int[] { 0, 1 };

            for (int i = 0; i < 1024 * 1024; i++)
            {
                bool result1_1 = false;
                bool result1_2 = false;

                bool result2_1 = false;
                bool result2_2 = false;

                random.Shuffle(flags);
                foreach (var index in flags)
                {
                    if (index == 0)
                    {
                        sw1.Start();
                        result1_1 = Signature.CheckBase64(hash);
                        result1_2 = Signature.CheckBase64(hash2);
                        sw1.Stop();
                    }
                    else if (index == 1)
                    {
                        sw2.Start();
                        result2_1 = Benchmark.CheckBase64_1(hash);
                        result2_2 = Benchmark.CheckBase64_1(hash2);
                        sw2.Stop();
                    }
                }

                Assert.IsTrue(result1_1 == result2_1);
                Assert.IsTrue(result1_2 == result2_2);
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Native CheckBase64: " + sw1.Elapsed.ToString());
            sb.AppendLine("Unsafe CheckBase64: " + sw2.Elapsed.ToString());

            Console.WriteLine(sb.ToString());
        }

        private unsafe static bool CheckBase64_1(string value)
        {
            fixed (char* p_value = value)
            {
                var t_value = p_value;

                for (int i = value.Length - 1; i >= 0; i--)
                {
                    if (!('A' <= *t_value && *t_value <= 'Z')
                        && !('a' <= *t_value && *t_value <= 'z')
                        && !('0' <= *t_value && *t_value <= '9')
                        && !(*t_value == '-' || *t_value == '_')) return false;

                    t_value++;
                }
            }

            return true;
        }
    }
}

using System;
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
        public void Xor()
        {
            Random random = new Random();

            Stopwatch sw1 = new Stopwatch();
            Stopwatch sw2 = new Stopwatch();

            var flags = new int[] { 0, 1 };

            for (int i = 0; i < 1024 * 256 * 3; i++)
            {
                byte[] x;
                byte[] y;

                if (random.Next(0, 2) == 0)
                {
                    var length = random.Next(0, 1024);
                    x = new byte[length];
                    y = new byte[length];
                }
                else
                {
                    x = new byte[random.Next(0, 1024)];
                    y = new byte[random.Next(0, 1024)];
                }

                random.NextBytes(x);
                random.NextBytes(y);

                byte[] result = null;
                byte[] result2 = null;

                random.Shuffle(flags);
                foreach (var index in flags)
                {
                    if (index == 0)
                    {
                        sw1.Start();
                        result = Native.Xor(x, y);
                        sw1.Stop();
                    }
                    else if (index == 1)
                    {
                        sw2.Start();
                        result2 = Benchmark.Xor(x, y);
                        sw2.Stop();
                    }
                }

                Assert.IsTrue(Native.Equals(result, result2));
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Native Xor: " + sw1.Elapsed.ToString());
            sb.AppendLine("Unsafe Xor: " + sw2.Elapsed.ToString());

            Console.WriteLine(sb.ToString());
        }

        private unsafe static byte[] Xor(byte[] x, byte[] y)
        {
            if (x.Length != y.Length)
            {
                if (x.Length < y.Length)
                {
                    fixed (byte* p_x = x, p_y = y)
                    {
                        byte* t_x = p_x, t_y = p_y;

                        byte[] buffer = new byte[y.Length];
                        int length = x.Length;

                        fixed (byte* p_buffer = buffer)
                        {
                            byte* t_buffer = p_buffer;

                            for (int i = (length / 8) - 1; i >= 0; i--, t_x += 8, t_y += 8, t_buffer += 8)
                            {
                                *((long*)t_buffer) = *((long*)t_x) ^ *((long*)t_y);
                            }

                            if ((length & 4) != 0)
                            {
                                *((int*)t_buffer) = *((int*)t_x) ^ *((int*)t_y);
                                t_x += 4; t_y += 4; t_buffer += 4;
                            }

                            if ((length & 2) != 0)
                            {
                                *((short*)t_buffer) = (short)(*((short*)t_x) ^ *((short*)t_y));
                                t_x += 2; t_y += 2; t_buffer += 2;
                            }

                            if ((length & 1) != 0)
                            {
                                *((byte*)t_buffer) = (byte)(*((byte*)t_x) ^ *((byte*)t_y));
                            }
                        }

                        Array.Copy(y, x.Length, buffer, x.Length, y.Length - x.Length);

                        return buffer;
                    }
                }
                else
                {
                    fixed (byte* p_x = x, p_y = y)
                    {
                        byte* t_x = p_x, t_y = p_y;

                        byte[] buffer = new byte[x.Length];
                        int length = y.Length;

                        fixed (byte* p_buffer = buffer)
                        {
                            byte* t_buffer = p_buffer;

                            for (int i = (length / 8) - 1; i >= 0; i--, t_x += 8, t_y += 8, t_buffer += 8)
                            {
                                *((long*)t_buffer) = *((long*)t_x) ^ *((long*)t_y);
                            }

                            if ((length & 4) != 0)
                            {
                                *((int*)t_buffer) = *((int*)t_x) ^ *((int*)t_y);
                                t_x += 4; t_y += 4; t_buffer += 4;
                            }

                            if ((length & 2) != 0)
                            {
                                *((short*)t_buffer) = (short)(*((short*)t_x) ^ *((short*)t_y));
                                t_x += 2; t_y += 2; t_buffer += 2;
                            }

                            if ((length & 1) != 0)
                            {
                                *((byte*)t_buffer) = (byte)(*((byte*)t_x) ^ *((byte*)t_y));
                            }
                        }

                        Array.Copy(x, y.Length, buffer, y.Length, x.Length - y.Length);

                        return buffer;
                    }
                }
            }
            else
            {
                fixed (byte* p_x = x, p_y = y)
                {
                    byte* t_x = p_x, t_y = p_y;

                    byte[] buffer = new byte[x.Length];
                    int length = x.Length;

                    fixed (byte* p_buffer = buffer)
                    {
                        byte* t_buffer = p_buffer;

                        for (int i = (length / 8) - 1; i >= 0; i--, t_x += 8, t_y += 8, t_buffer += 8)
                        {
                            *((long*)t_buffer) = *((long*)t_x) ^ *((long*)t_y);
                        }

                        if ((length & 4) != 0)
                        {
                            *((int*)t_buffer) = *((int*)t_x) ^ *((int*)t_y);
                            t_x += 4; t_y += 4; t_buffer += 4;
                        }

                        if ((length & 2) != 0)
                        {
                            *((short*)t_buffer) = (short)(*((short*)t_x) ^ *((short*)t_y));
                            t_x += 2; t_y += 2; t_buffer += 2;
                        }

                        if ((length & 1) != 0)
                        {
                            *((byte*)t_buffer) = (byte)(*((byte*)t_x) ^ *((byte*)t_y));
                        }
                    }

                    return buffer;
                }
            }
        }
    }
}

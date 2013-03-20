using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Library;
namespace Test
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            try
            {
                using (ReedSolomon_Utility.FEC m = new ReedSolomon_Utility.FEC(128, 256))
                {
                    Random rand = new Random();

                    var buffList = new List<byte[]>();
                    for (int i = 0; i < 128; i++)
                    {
                        buffList.Add(NetworkConverter.GetBytes(rand.Next()));
                    }

                    var buffList2 = new List<byte[]>();
                    for (int i = 0; i < 256; i++)
                    {
                        buffList2.Add(new byte[4]);
                    }

                    List<int> intList = new List<int>();
                    for (int i = 0; i < 256; i++)
                    {
                        intList.Add(i);
                    }

                    m.Encode(buffList.ToArray(), buffList2.ToArray(), intList.ToArray(), 4);

                    var buffList3 = new List<byte[]>();

                    for (int i = 0; i < 64; i++)
                    {
                        buffList3.Add(buffList2[i]);
                    }

                    for (int i = 0; i < 64; i++)
                    {
                        buffList3.Add(buffList2[128 + i]);
                    }

                    List<int> intList2 = new List<int>();
                    for (int i = 0; i < 64; i++)
                    {
                        intList2.Add(i);
                    }

                    for (int i = 0; i < 64; i++)
                    {
                        intList2.Add(128 + i);
                    }

                    m.Decode(buffList3.ToArray(), intList2.ToArray(), 4);

                    for (int i = buffList.Count; i < buffList.Count; i++)
                    {
                        if (!Collection.Equals(buffList[i], buffList3[i]))
                        {
                            goto End;
                        }
                    }

                    return;

                End: ;
                }
            }
            catch (Exception)
            {

            }
        }
    }
}

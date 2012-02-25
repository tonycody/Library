using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Library.Correction;
using NUnit.Framework;

namespace Library.UnitTest
{
    [TestFixture, Category("Library.Correction")]
    public class Test_Library_Correction
    {
        [Test]
        public void Test_ReedSolomon()
        {
            ReedSolomon pc = new ReedSolomon(8, 128, 256, 4);
            Random rand = new Random();

            List<ArraySegment<byte>> buffList = new List<ArraySegment<byte>>();
            for (int i = 0; i < 128; i++)
            {
                buffList.Add(new ArraySegment<byte>(NetworkConverter.GetBytes(rand.Next()), 0, 4));
            }

            List<ArraySegment<byte>> buffList2 = new List<ArraySegment<byte>>();
            for (int i = 0; i < 256; i++)
            {
                buffList2.Add(new ArraySegment<byte>(new byte[4], 0, 4));
            }

            List<int> intList = new List<int>();
            for (int i = 0; i < 256; i++)
            {
                intList.Add(i);
            }

            pc.Encode(buffList.ToArray(), buffList2.ToArray(), intList.ToArray());

            List<ArraySegment<byte>> buffList3 = new List<ArraySegment<byte>>();

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

            pc.Decode(buffList3.ToArray(), intList2.ToArray());

            for (int i = buffList.Count; i < buffList.Count; i++)
            {
                Assert.IsTrue(Collection.Equals(buffList[i].Array, buffList[i].Offset, buffList3[i].Array, buffList3[i].Offset, 4), "ReedSolomon");
            }
        }
    }
}

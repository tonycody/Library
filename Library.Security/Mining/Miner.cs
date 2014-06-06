using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Library.Security
{
    public class Miner
    {
        private CashAlgorithm _cashAlgorithm;
        private TimeSpan _computeTime;

        public Miner(CashAlgorithm cashAlgorithm, TimeSpan computeTime)
        {
            _cashAlgorithm = cashAlgorithm;
            _computeTime = computeTime;
        }

        public CashAlgorithm CashAlgorithm
        {
            get
            {
                return _cashAlgorithm;
            }
        }

        public TimeSpan ComputeTime
        {
            get
            {
                return _computeTime;
            }
        }

        public static Cash Create(Miner miner, Stream stream)
        {
            return new Cash(miner, stream);
        }

        public static int Verify(Cash cash, Stream stream)
        {
            return cash.Verify(stream);
        }
    }
}

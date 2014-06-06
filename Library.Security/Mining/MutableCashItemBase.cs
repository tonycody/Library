using System;
using System.IO;
using System.Runtime.Serialization;

namespace Library.Security
{
    [DataContract(Name = "CashItemBase", Namespace = "http://Library/Security")]
    public abstract class MutableCashItemBase<T> : ItemBase<T>, ICash
        where T : MutableCashItemBase<T>
    {
        public virtual void CreateCash(Miner miner)
        {
            if (miner == null)
            {
                this.Cash = null;
            }
            else
            {
                using (var stream = this.GetCashStream())
                {
                    this.Cash = new Cash(miner, stream);
                }
            }
        }

        public virtual int VerifyCash()
        {
            if (this.Cash == null)
            {
                return 0;
            }
            else
            {
                using (var stream = this.GetCashStream())
                {
                    return this.Cash.Verify(stream);
                }
            }
        }

        protected abstract Stream GetCashStream();

        [DataMember(Name = "Cash")]
        public abstract Cash Cash { get; protected set; }
    }
}

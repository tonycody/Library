using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace Library
{
    [DataContract(Name = "SafeInteger", Namespace = "http://Library")]
    public class SafeInteger : IEquatable<SafeInteger>
    {
        [DataMember(Name = "Value")]
        private long _value;

        public SafeInteger()
        {
            _value = 0;
        }

        public SafeInteger(long value)
        {
            _value = value;
        }

        private long Value
        {
            get
            {
                return Interlocked.Read(ref _value);
            }
        }

        public override int GetHashCode()
        {
            return this.Value.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((SafeInteger)obj == null || !(obj is SafeInteger)) return false;

            return this.Equals((SafeInteger)obj);
        }

        public bool Equals(SafeInteger other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            return this.Value == other.Value;
        }

        // ==
        public static bool operator ==(SafeInteger x, SafeInteger y)
        {
            return x.Value == y.Value;
        }

        // !=
        public static bool operator !=(SafeInteger x, SafeInteger y)
        {
            return x.Value != y.Value;
        }

        // <
        public static bool operator <(SafeInteger x, SafeInteger y)
        {
            return x.Value < y.Value;
        }

        // >
        public static bool operator >(SafeInteger x, SafeInteger y)
        {
            return x.Value > y.Value;
        }

        // <=
        public static bool operator <=(SafeInteger x, SafeInteger y)
        {
            return x.Value <= y.Value;
        }

        // >=
        public static bool operator >=(SafeInteger x, SafeInteger y)
        {
            return x.Value >= y.Value;
        }

        // explicit
        public static implicit operator SafeInteger(long i) { return new SafeInteger(i); }
        public static implicit operator long(SafeInteger safeInteger) { return safeInteger.Value; }

        public long Increment()
        {
            return Interlocked.Increment(ref _value);
        }

        public long Decrement()
        {
            return Interlocked.Decrement(ref _value);
        }

        public long Add(long value)
        {
            return Interlocked.Add(ref _value, value);
        }

        public long Subtract(long value)
        {
            return Interlocked.Add(ref _value, -value);
        }

        public long Exchange(long value)
        {
            return Interlocked.Exchange(ref _value, value);
        }
    }
}

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
            if ((object)obj == null || !(obj is long)) return false;

            return this.Equals((long)obj);
        }

        public bool Equals(SafeInteger other)
        {
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
        public static explicit operator SafeInteger(long i) { return new SafeInteger(i); }
        public static explicit operator long(SafeInteger safeInteger) { return safeInteger.Value; }
        public static explicit operator SafeInteger(int i) { return new SafeInteger((long)i); }
        public static explicit operator int(SafeInteger safeInteger) { return (int)safeInteger.Value; }

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

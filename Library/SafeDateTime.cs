using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;

namespace Library
{
    public class SafeDateTime : IEquatable<SafeDateTime>
    {
        private long _ticks;

        public SafeDateTime()
        {
            _ticks = 0;
        }

        public SafeDateTime(DateTime value)
        {
            _ticks = value.Ticks;
        }

        private DateTime Value
        {
            get
            {
                return new DateTime(Interlocked.Read(ref _ticks));
            }
        }

        public override int GetHashCode()
        {
            return this.Value.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if ((SafeDateTime)obj == null || !(obj is SafeDateTime)) return false;

            return this.Equals((SafeDateTime)obj);
        }

        public bool Equals(SafeDateTime other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            return this.Value == other.Value;
        }

        // ==
        public static bool operator ==(SafeDateTime x, SafeDateTime y)
        {
            return x.Value == y.Value;
        }

        // !=
        public static bool operator !=(SafeDateTime x, SafeDateTime y)
        {
            return x.Value != y.Value;
        }

        // <
        public static bool operator <(SafeDateTime x, SafeDateTime y)
        {
            return x.Value < y.Value;
        }

        // >
        public static bool operator >(SafeDateTime x, SafeDateTime y)
        {
            return x.Value > y.Value;
        }

        // <=
        public static bool operator <=(SafeDateTime x, SafeDateTime y)
        {
            return x.Value <= y.Value;
        }

        // >=
        public static bool operator >=(SafeDateTime x, SafeDateTime y)
        {
            return x.Value >= y.Value;
        }

        // implicit
        public static implicit operator SafeDateTime(DateTime dateTime) { return new SafeDateTime(dateTime); }
        public static implicit operator DateTime(SafeDateTime safeDateTime) { return safeDateTime.Value; }

        public DateTime Exchange(DateTime value)
        {
            return new DateTime(Interlocked.Exchange(ref _ticks, value.Ticks));
        }
    }
}

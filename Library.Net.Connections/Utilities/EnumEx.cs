using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Library.Net.Connections
{
    internal class EnumEx<TEnum>
        where TEnum : struct
    {
        public static TEnum Parse(string value)
        {
            var list = Enum.GetNames(typeof(TEnum)).ToList();
            var sb = new StringBuilder();

            foreach (var line in value.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim()))
            {
                if (!list.Contains(line)) continue;

                if (sb.Length == 0) sb.Append(line);
                else sb.Append(", " + line);
            }

            return (TEnum)Enum.Parse(typeof(TEnum), sb.ToString());
        }
    }
}

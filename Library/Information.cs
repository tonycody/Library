using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Runtime.Serialization;
using System.IO;
using System.Xml;

namespace Library
{
    [DataContract(Name = "InformationContext", Namespace = "http://Library")]
    public struct InformationContext
    {
        private string _key;
        private object _value;

        public InformationContext(string key, object value)
        {
            _key = key;
            _value = value;
        }

        [DataMember(Name = "Key")]
        public string Key
        {
            get
            {
                return _key;
            }
            private set
            {
                _key = value;
            }
        }

        [DataMember(Name = "Value")]
        public object Value
        {
            get
            {
                return _value;
            }
            private set
            {
                _value = value;
            }
        }
    }

    //[DataContract(Name = "InformationCollection", Namespace = "http://Library")]
    public class Information : IEnumerable<InformationContext>
    {
        private IList<InformationContext> _contextList;
        private Dictionary<string, InformationContext> _dic;

        public Information(IEnumerable<InformationContext> contextList)
        {
            _contextList = contextList.ToList();
            _dic = new Dictionary<string, InformationContext>();

            foreach (var item in contextList)
            {
                _dic.Add(item.Key, item);
            }
        }

        public object this[string propertyName]
        {
            get
            {
                if (_dic.ContainsKey(propertyName))
                    return _dic[propertyName].Value;

                return null;
            }
        }

        public bool Contains(string propertyName)
        {
            return _dic.ContainsKey(propertyName);
        }

        public IEnumerator<InformationContext> GetEnumerator()
        {
            return _contextList.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}

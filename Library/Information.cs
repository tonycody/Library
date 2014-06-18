using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

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

        public override string ToString()
        {
            return string.Format("Key = {0}, Value = {1}", this.Key, this.Value);
        }
    }

    [DataContract(Name = "Information", Namespace = "http://Library")]
    public class Information : IEnumerable<InformationContext>
    {
        [DataMember(Name = "Contexts")]
        private List<InformationContext> _contexts;

        public Information(IEnumerable<InformationContext> contexts)
        {
            _contexts = new List<InformationContext>();

            foreach (var item in contexts)
            {
                _contexts.Add(item);
            }
        }

        public object this[string propertyName]
        {
            get
            {
                return _contexts.First(n => n.Key == propertyName).Value;
            }
        }

        public bool Contains(string propertyName)
        {
            return _contexts.Any(n => n.Key == propertyName);
        }

        public IEnumerator<InformationContext> GetEnumerator()
        {
            foreach (var item in _contexts)
            {
                yield return item;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}

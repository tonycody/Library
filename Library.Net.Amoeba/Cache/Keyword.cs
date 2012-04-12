using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using Library;
using Library.Io;

namespace Library.Net.Amoeba
{
    [DataContract(Name = "Keyword", Namespace = "http://Library/Net/Amoeba")]
    public class Keyword : ItemBase<Keyword>, IKeyword, IThisLock
    {
        private enum SerializeId : byte
        {
            Value = 0,
            HashAlgorithm = 1,
        }

        public Keyword()
        {

        }

        private string _value = null;
        private HashAlgorithm _hashAlgorithm;
        private byte[] _valueBytes = null;
        private byte[] _hash = null;
        private int _hashCode = 0;
        private object _thisLock;
        private static object _thisStaticLock = new object();

        public const int MaxValueLength = 256;

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                Encoding encoding = new UTF8Encoding(false);
                byte[] lengthBuffer = new byte[4];

                for (; ; )
                {
                    if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                    int length = NetworkConverter.ToInt32(lengthBuffer);
                    byte id = (byte)stream.ReadByte();

                    using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                    {
                        if (id == (byte)SerializeId.Value)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.Value = reader.ReadToEnd();
                            }
                        }
                        else if (id == (byte)SerializeId.HashAlgorithm)
                        {
                            using (StreamReader reader = new StreamReader(rangeStream, encoding))
                            {
                                this.HashAlgorithm = (HashAlgorithm)Enum.Parse(typeof(HashAlgorithm), reader.ReadToEnd());
                            }
                        }
                    }
                }
            }
        }

        public override Stream Export(BufferManager bufferManager)
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                List<Stream> streams = new List<Stream>();
                Encoding encoding = new UTF8Encoding(false);

                // Value
                if (this.Value != null)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                    using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                    {
                        writer.Write(this.Value);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.Value);

                    streams.Add(bufferStream);
                }

                // HashAlgorithm
                if (this.HashAlgorithm != 0)
                {
                    BufferStream bufferStream = new BufferStream(bufferManager);
                    bufferStream.SetLength(5);
                    bufferStream.Seek(5, SeekOrigin.Begin);

                    using (CacheStream cacheStream = new CacheStream(bufferStream, 1024, true, bufferManager))
                    using (StreamWriter writer = new StreamWriter(cacheStream, encoding))
                    {
                        writer.Write(this.HashAlgorithm);
                    }

                    bufferStream.Seek(0, SeekOrigin.Begin);
                    bufferStream.Write(NetworkConverter.GetBytes((int)bufferStream.Length - 5), 0, 4);
                    bufferStream.WriteByte((byte)SerializeId.HashAlgorithm);

                    streams.Add(bufferStream);
                }

                return new AddStream(streams);
            }
        }

        public override int GetHashCode()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return _hashCode;
            }
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Keyword)) return false;

            return this.Equals((Keyword)obj);
        }

        public override bool Equals(Keyword other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;
            if (this.GetHashCode() != other.GetHashCode()) return false;

            if ((this.Value != other.Value)
                || (this.HashAlgorithm != other.HashAlgorithm))
            {
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                return this.Value;
            }
        }

        public override Keyword DeepClone()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                using (var bufferManager = new BufferManager())
                using (var stream = this.Export(bufferManager))
                {
                    return Keyword.Import(stream, bufferManager);
                }
            }
        }

        private void SetHash()
        {
            using (DeadlockMonitor.Lock(this.ThisLock))
            {
                if (_valueBytes != null && _hashAlgorithm == HashAlgorithm.Sha512)
                {
                    _hash = Sha512.ComputeHash(_valueBytes);
                }
                else
                {
                    _hash = null;
                }

                if (_hash != null && _hash.Length != 0)
                {
                    try
                    {
                        if (_hash.Length >= 4) _hashCode = Math.Abs(BitConverter.ToInt32(_hash, 0));
                        else if (_hash.Length >= 2) _hashCode = BitConverter.ToUInt16(_hash, 0);
                        else _hashCode = _hash[0];
                    }
                    catch
                    {
                        _hashCode = 0;
                    }
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }

        #region IKeyword メンバ

        [DataMember(Name = "Value")]
        public string Value
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _value;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (value != null && value.Length > Keyword.MaxValueLength)
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        if (value != null)
                        {
                            string temp = value;
                            temp = temp.Normalize(NormalizationForm.FormD);
                            temp = temp.Trim();

                            List<byte> blist = new List<byte>();

                            // [A-Z]を小文字に変換,[a-z0-9_]だけ許可
                            foreach (var b in Encoding.ASCII.GetBytes(temp))
                            {
                                if (b == 0x5f)
                                {
                                    blist.Add(b);
                                }
                                else if (b >= 0x61 && b <= 0x7A)
                                {
                                    blist.Add(b);
                                }
                                else if (b >= 0x30 && b <= 0x39)
                                {
                                    blist.Add(b);
                                }
                                else if (b >= 0x41 && b <= 0x5A)
                                {
                                    blist.Add((byte)(b + 0x20));
                                }
                            }

                            if (blist.Count == 0)
                            {
                                _valueBytes = null;
                            }
                            else
                            {
                                _valueBytes = blist.ToArray();
                            }
                        }
                        else
                        {
                            _valueBytes = null;
                        }
                    }

                    if (_valueBytes != null) _value = Encoding.ASCII.GetString(_valueBytes);
                    else _value = null;

                    this.SetHash();
                }
            }
        }

        public byte[] Hash
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _hash;
                }
            }
        }

        #endregion

        #region IHashAlgorithm メンバ

        [DataMember(Name = "HashAlgorithm")]
        public HashAlgorithm HashAlgorithm
        {
            get
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    return _hashAlgorithm;
                }
            }
            set
            {
                using (DeadlockMonitor.Lock(this.ThisLock))
                {
                    if (!Enum.IsDefined(typeof(HashAlgorithm), value))
                    {
                        throw new ArgumentException();
                    }
                    else
                    {
                        _hashAlgorithm = value;
                    }

                    this.SetHash();
                }
            }
        }

        #endregion

        #region IThisLock メンバ

        public object ThisLock
        {
            get
            {
                using (DeadlockMonitor.Lock(_thisStaticLock))
                {
                    if (_thisLock == null)
                        _thisLock = new object();

                    return _thisLock;
                }
            }
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Library.Io;

namespace Library.Security
{
    [DataContract(Name = "Cash", Namespace = "http://Library/Security")]
    public sealed class Cash : ItemBase<Cash>
    {
        private enum SerializeId : byte
        {
            CashAlgorithm = 0,
            Key = 1,
        }

        private volatile CashAlgorithm _cashAlgorithm = 0;
        private volatile byte[] _key;

        private volatile int _hashCode;

        public static readonly int MaxKeyLength = 256;
        public static readonly int MaxValueLength = 64;

        private Cash(CashAlgorithm cashAlgorithm, byte[] value, TimeSpan timeout)
        {
            this.CashAlgorithm = cashAlgorithm;

            if (cashAlgorithm == CashAlgorithm.Version1)
            {
                this.Key = Cash_Utilities_1.Create(value, timeout);
            }
        }

        protected override void Initialize()
        {

        }

        protected override void ProtectedImport(Stream stream, BufferManager bufferManager, int count)
        {
            byte[] lengthBuffer = new byte[4];

            for (; ; )
            {
                if (stream.Read(lengthBuffer, 0, lengthBuffer.Length) != lengthBuffer.Length) return;
                int length = NetworkConverter.ToInt32(lengthBuffer);
                byte id = (byte)stream.ReadByte();

                using (RangeStream rangeStream = new RangeStream(stream, stream.Position, length, true))
                {
                    if (id == (byte)SerializeId.CashAlgorithm)
                    {
                        this.CashAlgorithm = (CashAlgorithm)Enum.Parse(typeof(CashAlgorithm), ItemUtilities.GetString(rangeStream));
                    }
                    else if (id == (byte)SerializeId.Key)
                    {
                        this.Key = ItemUtilities.GetByteArray(rangeStream);
                    }
                }
            }
        }

        protected override Stream Export(BufferManager bufferManager, int count)
        {
            BufferStream bufferStream = new BufferStream(bufferManager);

            // CashAlgorithm
            if (this.CashAlgorithm != 0)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.CashAlgorithm, this.CashAlgorithm.ToString());
            }
            // Key
            if (this.Key != null)
            {
                ItemUtilities.Write(bufferStream, (byte)SerializeId.Key, this.Key);
            }

            bufferStream.Seek(0, SeekOrigin.Begin);
            return bufferStream;
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        public override bool Equals(object obj)
        {
            if ((object)obj == null || !(obj is Cash)) return false;

            return this.Equals((Cash)obj);
        }

        public override bool Equals(Cash other)
        {
            if ((object)other == null) return false;
            if (object.ReferenceEquals(this, other)) return true;

            if (this.CashAlgorithm != other.CashAlgorithm
                || ((this.Key == null) != (other.Key == null)))
            {
                return false;
            }

            if (this.Key != null && other.Key != null)
            {
                if (!Unsafe.Equals(this.Key, other.Key)) return false;
            }

            return true;
        }

        public Cash Clone()
        {
            using (var stream = this.Export(BufferManager.Instance))
            {
                return Cash.Import(stream, BufferManager.Instance);
            }
        }

        public static Cash Create(CashAlgorithm cashAlgorithm, byte[] value, TimeSpan timeout)
        {
            return new Cash(cashAlgorithm, value, timeout);
        }

        public int Verify(byte[] value)
        {
            if (this.CashAlgorithm == Security.CashAlgorithm.Version1)
            {
                return Cash_Utilities_1.Verify(this.Key, value);
            }
            else
            {
                return -1;
            }
        }

        [DataMember(Name = "CashAlgorithm")]
        public CashAlgorithm CashAlgorithm
        {
            get
            {
                return _cashAlgorithm;
            }
            private set
            {
                if (!Enum.IsDefined(typeof(CashAlgorithm), value))
                {
                    throw new ArgumentException();
                }
                else
                {
                    _cashAlgorithm = value;
                }
            }
        }

        [DataMember(Name = "Key")]
        public byte[] Key
        {
            get
            {
                return _key;
            }
            private set
            {
                if (value != null && value.Length > Cash.MaxKeyLength)
                {
                    throw new ArgumentException();
                }
                else
                {
                    _key = value;
                }

                if (value != null)
                {
                    _hashCode = ItemUtilities.GetHashCode(value);
                }
                else
                {
                    _hashCode = 0;
                }
            }
        }

        private class Cash_Utilities_1
        {
            private static string _path;

            static Cash_Utilities_1()
            {
                OperatingSystem osInfo = Environment.OSVersion;

                if (osInfo.Platform == PlatformID.Win32NT)
                {
                    if (System.Environment.Is64BitProcess)
                    {
                        _path = "Assembly/Hashcash_x64.exe";
                    }
                    else
                    {
                        _path = "Assembly/Hashcash_x86.exe";
                    }
                }
            }

            public static byte[] Create(byte[] value, TimeSpan timeout)
            {
                if (value == null) throw new ArgumentNullException("value");
                if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException("timeout");

                var info = new ProcessStartInfo(_path);
                info.CreateNoWindow = true;
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;

                info.Arguments = string.Format("hashcash1 create {0} {1}", NetworkConverter.ToHexString(value), (int)timeout.TotalSeconds);

                using (var process = Process.Start(info))
                {
                    process.PriorityClass = ProcessPriorityClass.Idle;

                    try
                    {
                        var result = process.StandardOutput.ReadLine();

                        process.WaitForExit();
                        if (process.ExitCode != 0) return null;

                        return NetworkConverter.FromHexString(result);
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }
            }

            public static int Verify(byte[] key, byte[] value)
            {
                if (key == null) throw new ArgumentNullException("key");
                if (value == null) throw new ArgumentNullException("value");
                if (key.Length != 64) throw new ArgumentOutOfRangeException("key");

                var info = new ProcessStartInfo(_path);
                info.CreateNoWindow = true;
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;

                info.Arguments = string.Format("hashcash1 verify {0} {1}", NetworkConverter.ToHexString(key), NetworkConverter.ToHexString(value));

                using (var process = Process.Start(info))
                {
                    process.PriorityClass = ProcessPriorityClass.Idle;

                    try
                    {
                        var result = process.StandardOutput.ReadLine();

                        process.WaitForExit();
                        if (process.ExitCode != 0) return -1;

                        return int.Parse(result);
                    }
                    catch (Exception)
                    {
                        return -1;
                    }
                }
            }
        }
    }
}

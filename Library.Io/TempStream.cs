using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Library.Io
{
    public class TempStream : Stream
    {
        private static readonly string _tempDirectory;

        static TempStream()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "Stream");

            try
            {
                try
                {
                    if (!Directory.Exists(_tempDirectory))
                    {
                        Directory.CreateDirectory(_tempDirectory);
                    }
                }
                catch (Exception)
                {

                }

                foreach (var path in Directory.GetFiles(_tempDirectory))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception)
                    {

                    }
                }
            }
            catch (Exception)
            {

            }
        }

        private FileStream _stream;

        private bool _disposed;

        public TempStream()
        {
            _stream = TempStream.GetStream(_tempDirectory);
        }

        private static readonly ThreadLocal<Random> _threadLocalRandom = new ThreadLocal<Random>(() =>
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                var buffer = new byte[4];
                rng.GetBytes(buffer);

                return new Random(BitConverter.ToInt32(buffer, 0));
            }
        });

        private static readonly ThreadLocal<byte[]> _threadLocalBuffer = new ThreadLocal<byte[]>(() => new byte[8]);

        private static FileStream GetStream(string directoryPath)
        {
            var buffer = _threadLocalBuffer.Value;

            for (; ; )
            {
                _threadLocalRandom.Value.NextBytes(buffer);

                string text = string.Format(
                    @"{0}\{1}",
                    directoryPath,
                    NetworkConverter.ToBase64UrlString(buffer));

                if (!File.Exists(text))
                {
                    try
                    {
                        return new FileStream(text, FileMode.CreateNew);
                    }
                    catch (DirectoryNotFoundException)
                    {
                        throw;
                    }
                    catch (IOException)
                    {

                    }
                }
            }
        }

        public override bool CanRead
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.CanRead;
            }
        }

        public override bool CanWrite
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.CanWrite;
            }
        }

        public override bool CanSeek
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.CanSeek;
            }
        }

        public override long Position
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.Position;
            }
            set
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                _stream.Position = value;
            }
        }

        public override long Length
        {
            get
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                return _stream.Length;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _stream.SetLength(value);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            return _stream.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _stream.Write(buffer, offset, count);
        }

        public override void Flush()
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            _stream.Flush();
        }

        public override void Close()
        {
            if (_disposed) return;

            this.Flush();
            base.Close();
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (_disposed) return;
                _disposed = true;

                if (disposing)
                {
                    string path = null;

                    if (_stream != null)
                    {
                        path = _stream.Name;

                        try
                        {
                            _stream.Dispose();
                        }
                        catch (Exception)
                        {

                        }

                        _stream = null;
                    }

                    if (path != null)
                    {
                        try
                        {
                            File.Delete(path);
                        }
                        catch (Exception)
                        {

                        }
                    }
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }
    }
}

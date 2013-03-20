﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;

namespace Library.Correction
{
#if false
    public class ReedSolomon
    {
        private int _k;
        private int _n;
        private int _threadCount;

        private byte[] _encMatrix;
        private ReedSolomon.Math _fecMath;
        private object _thisLock = new object();

        public ReedSolomon(int gfBits, int k, int n, int threadCount)
        {
            _fecMath = new Math(gfBits);
            _k = k;
            _n = n;
            _threadCount = threadCount;

            _encMatrix = _fecMath.CreateEncodeMatrix(k, n);
        }

        public void Encode(ArraySegment<byte>[] src, ArraySegment<byte>[] repair, int[] index)
        {
            byte[][] srcBufs = new byte[src.Length][];
            int[] srcOffs = new int[src.Length];
            byte[][] repairBufs = new byte[repair.Length][];
            int[] repairOffs = new int[repair.Length];

            for (int i = 0; i < srcBufs.Length; i++)
            {
                srcBufs[i] = src[i].Array;
                srcOffs[i] = src[i].Offset;
            }

            for (int i = 0; i < repairBufs.Length; i++)
            {
                repairBufs[i] = repair[i].Array;
                repairOffs[i] = repair[i].Offset;
            }

            this.Encode(srcBufs, srcOffs, repairBufs, repairOffs, index, src[0].Count);
        }

        private void Encode(byte[][] src, int[] srcOff, byte[][] repair, int[] repairOff, int[] index, int packetLength)
        {
            Parallel.For(0, repair.Length, new ParallelOptions() { MaxDegreeOfParallelism = _threadCount }, row =>
            {
                Thread.CurrentThread.IsBackground = true;
                Thread.CurrentThread.Priority = ThreadPriority.Lowest;

                // *remember* indices start at 0, k starts at 1.
                if (index[row] < _k)
                {
                    // < k, systematic so direct copy.
                    System.Array.Copy(src[index[row]], srcOff[index[row]], repair[row], repairOff[row], packetLength);
                }
                else
                {
                    // index[row] >= k && index[row] < n
                    int pos = index[row] * _k;
                    Array.Clear(repair[row], repairOff[row], packetLength);

                    for (int col = 0; col < _k; col++)
                    {
                        _fecMath.AddMul(repair[row], repairOff[row], src[col], srcOff[col], (byte)_encMatrix[pos + col], packetLength);
                    }
                }
            });
        }

        public void Decode(ArraySegment<byte>[] pkts, int[] index)
        {
            ReedSolomon.CopyShuffle(pkts, index, _k);

            byte[][] bufs = new byte[pkts.Length][];
            int[] offs = new int[pkts.Length];

            for (int i = 0; i < bufs.Length; i++)
            {
                bufs[i] = pkts[i].Array;
                offs[i] = pkts[i].Offset;
            }

            this.Decode(bufs, offs, index, pkts[0].Count);
        }
  
        private void Decode(byte[][] pkts, int[] pktsOff, int[] index, int packetLength)
        {
            byte[] decMatrix = _fecMath.CreateDecodeMatrix(_encMatrix, index, _k, _n);

            // do the actual decoding..
            byte[][] tmpPkts = new byte[_k][];

            Parallel.For(0, _k, new ParallelOptions() { MaxDegreeOfParallelism = _threadCount }, row =>
            {
                Thread.CurrentThread.IsBackground = true;
                Thread.CurrentThread.Priority = ThreadPriority.Lowest;

                if (index[row] >= _k)
                {
                    tmpPkts[row] = new byte[packetLength];

                    for (int col = 0; col < _k; col++)
                    {
                        _fecMath.AddMul(tmpPkts[row], 0, pkts[col], pktsOff[col], (byte)decMatrix[row * _k + col], packetLength);
                    }
                }
            });

            // move pkts to their final destination
            for (int row = 0; row < _k; row++)
            {
                if (index[row] >= _k)
                {
                    // only copy those actually decoded.
                    System.Array.Copy(tmpPkts[row], 0, pkts[row], pktsOff[row], packetLength);
                    index[row] = row;
                }
            }
        }

        private static void CopyShuffle(ArraySegment<byte>[] pkts, int[] index, int k)
        {
            byte[] buffer = null;

            for (int i = 0; i < k; )
            {
                if (index[i] >= k || index[i] == i)
                {
                    i++;
                }
                else
                {
                    // put pkts in the right position (first check for conflicts).
                    int c = index[i];

                    if (index[c] == c)
                    {
                        throw new ArgumentException("Shuffle Error: Duplicate indexes at " + i);
                    }

                    // swap(index[c],index[i])
                    int tmp = index[i];
                    index[i] = index[c];
                    index[c] = tmp;

                    // swap(pkts[c],pkts[i])
                    if (buffer == null)
                    {
                        buffer = new byte[pkts[0].Count];
                    }

                    System.Array.Copy(pkts[i].Array, pkts[i].Offset, buffer, 0, buffer.Length);
                    System.Array.Copy(pkts[c].Array, pkts[c].Offset, pkts[i].Array, pkts[i].Offset, buffer.Length);
                    System.Array.Copy(buffer, 0, pkts[c].Array, pkts[c].Offset, buffer.Length);
                }
            }
        }

        private class Math
        {
            private int _gfBits;
            private int _gfSize;

            /**
             * Primitive polynomials - see Lin & Costello, Appendix A,
             * and  Lee & Messerschmitt, p. 453.
             */
            private static string[] _prim_polys = {    
                                      // gfBits         polynomial
            null,                     // 0              no code
            null,                     // 1              no code
            "111",                    // 2              1+x+x^2
            "1101",	                  // 3              1+x+x^3
            "11001",       	          // 4              1+x+x^4
            "101001",      	          // 5              1+x^2+x^5
            "1100001",     	          // 6              1+x+x^6
            "10010001",    	          // 7              1+x^3+x^7
            "101110001",              // 8              1+x^2+x^3+x^4+x^8
            "1000100001",             // 9              1+x^4+x^9
            "10010000001",            // 10             1+x^3+x^10
            "101000000001",           // 11             1+x^2+x^11
            "1100101000001",          // 12             1+x+x^4+x^6+x^12
            "11011000000001",         // 13             1+x+x^3+x^4+x^13
            "110000100010001",        // 14             1+x+x^6+x^10+x^14
            "1100000000000001",       // 15             1+x+x^15
            "11010000000010001"       // 16             1+x+x^3+x^12+x^16
            };

            /**
             * To speed up computations, we have tables for logarithm, exponent
             * and inverse of a number. If gfBits &lt;= 8, we use a table for
             * multiplication as well (it takes 64K, no big deal even on a PDA,
             * especially because it can be pre-initialized an put into a ROM!),
             * otherwhise we use a table of logarithms.
             */

            // index->poly form conversion table
            private byte[] _gf_exp;
            // Poly->index form conversion table
            private int[] _gf_log;
            // inverse of field elem.
            private byte[] _inverse;

            /**
             * gf_mul(x,y) multiplies two numbers. If gfBits&lt;=8, it is much
             * faster to use a multiplication table.
             *
             * USE_GF_MULC, GF_MULC0(c) and GF_ADDMULC(x) can be used when multiplying
             * many numbers by the same constant. In this case the first
             * call sets the constant, and others perform the multiplications.
             * A value related to the multiplication is held in a local variable
             * declared with USE_GF_MULC . See usage in addMul1().
             */
            private byte[][] _gf_mul_table;

            public Math()
                : this(8)
            {
            }

            public Math(int gfBits)
            {
                _gfBits = gfBits;
                _gfSize = ((1 << gfBits) - 1);

                _gf_exp = new byte[2 * _gfSize];
                _gf_log = new int[_gfSize + 1];
                _inverse = new byte[_gfSize + 1];

                if (gfBits < 2 || gfBits > 16)
                {
                    throw new ArgumentException("gfBits must be 2 .. 16");
                }
                GenerateGF();
                if (gfBits <= 8)
                {
                    InitMulTable();
                }
            }

            public void GenerateGF()
            {
                string primPoly = _prim_polys[_gfBits];
                byte mask = (byte)1;	// x ** 0 = 1
                _gf_exp[_gfBits] = (byte)0; // will be updated at the end of the 1st loop

                /*
                 * first, generate the (polynomial representation of) powers of \alpha,
                 * which are stored in gf_exp[i] = \alpha ** i .
                 * At the same time build gf_log[gf_exp[i]] = i .
                 * The first gfBits powers are simply bits shifted to the left.
                 */
                for (int i = 0; i < _gfBits; i++, mask <<= 1)
                {
                    _gf_exp[i] = mask;
                    _gf_log[_gf_exp[i]] = i;

                    /*
                     * If primPoly[i] == 1 then \alpha ** i occurs in poly-repr
                     * gf_exp[gfBits] = \alpha ** gfBits
                     */
                    if (primPoly[i] == '1')
                    {
                        _gf_exp[_gfBits] ^= mask;
                    }
                }

                /*
                 * now gf_exp[gfBits] = \alpha ** gfBits is complete, so can als
                 * compute its inverse.
                 */
                _gf_log[_gf_exp[_gfBits]] = _gfBits;

                /*
                 * Poly-repr of \alpha ** (i+1) is given by poly-repr of
                 * \alpha ** i shifted left one-bit and accounting for any
                 * \alpha ** gfBits term that may occur when poly-repr of
                 * \alpha ** i is shifted.
                 */
                mask = (byte)(1 << (_gfBits - 1));

                for (int i = _gfBits + 1; i < _gfSize; i++)
                {
                    if (_gf_exp[i - 1] >= mask)
                    {
                        _gf_exp[i] = (byte)(_gf_exp[_gfBits] ^ ((_gf_exp[i - 1] ^ mask) << 1));
                    }
                    else
                    {
                        _gf_exp[i] = (byte)(_gf_exp[i - 1] << 1);
                    }
                    _gf_log[_gf_exp[i]] = i;
                }

                /*
                 * log(0) is not defined, so use a special value
                 */
                _gf_log[0] = _gfSize;

                // set the extended gf_exp values for fast multiply
                for (int i = 0; i < _gfSize; i++)
                {
                    _gf_exp[i + _gfSize] = _gf_exp[i];
                }

                /*
                 * again special cases. 0 has no inverse. This used to
                 * be initialized to gfSize, but it should make no difference
                 * since noone is supposed to read from here.
                 */
                _inverse[0] = (byte)0;
                _inverse[1] = (byte)1;

                for (int i = 2; i <= _gfSize; i++)
                {
                    _inverse[i] = _gf_exp[_gfSize - _gf_log[i]];
                }
            }

            public void InitMulTable()
            {
                if (_gfBits <= 8)
                {
                    _gf_mul_table = new byte[_gfSize + 1][];

                    for (int i = 0; i < _gfSize + 1; i++)
                    {
                        _gf_mul_table[i] = new byte[_gfSize + 1];
                    }

                    for (int i = 0; i < _gfSize + 1; i++)
                    {
                        for (int j = 0; j < _gfSize + 1; j++)
                        {
                            _gf_mul_table[i][j] = _gf_exp[Modnn(_gf_log[i] + _gf_log[j])];
                        }
                    }

                    for (int i = 0; i < _gfSize + 1; i++)
                    {
                        _gf_mul_table[0][i] = _gf_mul_table[i][0] = (byte)0;
                    }
                }
            }

            public byte Modnn(int x)
            {
                while (x >= _gfSize)
                {
                    x -= _gfSize;
                    x = (x >> _gfBits) + (x & _gfSize);
                }

                return (byte)x;
            }

            public byte Mul(byte x, byte y)
            {
                if (_gfBits <= 8)
                {
                    return _gf_mul_table[x][y];
                }
                else
                {
                    if (x == 0 || y == 0)
                    {
                        return (byte)0;
                    }

                    return _gf_exp[_gf_log[x] + _gf_log[y]];
                }
            }

            public static byte[] CreateGFMatrix(int rows, int cols)
            {
                return new byte[rows * cols];
            }

            public void AddMul(byte[] dst, int dstPos, byte[] src, int srcPos, byte c, int len)
            {
                // nop, optimize
                if (c == 0)
                {
                    return;
                }

                int unroll = 16; // unroll the loop 16 times.
                int i = dstPos;
                int j = srcPos;
                int lim = dstPos + len;

                if (_gfBits <= 8)
                {
                    // use our multiplication table.
                    // Instead of doing gf_mul_table[c,x] for multiply, we'll save
                    // the gf_mul_table[c] to a local variable since it is going to
                    // be used many times.
                    byte[] gf_mulc = _gf_mul_table[c];

                    // Not sure if loop unrolling has any real benefit in Java, but 
                    // what the hey.
                    for (; i < lim && (lim - i) > unroll; i += unroll, j += unroll)
                    {
                        // dst ^= gf_mulc[x] is equal to mult then add (xor == add)

                        dst[i] ^= gf_mulc[src[j]];
                        dst[i + 1] ^= gf_mulc[src[j + 1]];
                        dst[i + 2] ^= gf_mulc[src[j + 2]];
                        dst[i + 3] ^= gf_mulc[src[j + 3]];
                        dst[i + 4] ^= gf_mulc[src[j + 4]];
                        dst[i + 5] ^= gf_mulc[src[j + 5]];
                        dst[i + 6] ^= gf_mulc[src[j + 6]];
                        dst[i + 7] ^= gf_mulc[src[j + 7]];
                        dst[i + 8] ^= gf_mulc[src[j + 8]];
                        dst[i + 9] ^= gf_mulc[src[j + 9]];
                        dst[i + 10] ^= gf_mulc[src[j + 10]];
                        dst[i + 11] ^= gf_mulc[src[j + 11]];
                        dst[i + 12] ^= gf_mulc[src[j + 12]];
                        dst[i + 13] ^= gf_mulc[src[j + 13]];
                        dst[i + 14] ^= gf_mulc[src[j + 14]];
                        dst[i + 15] ^= gf_mulc[src[j + 15]];
                    }

                    // final components
                    for (; i < lim; i++, j++)
                    {
                        dst[i] ^= gf_mulc[src[j]];
                    }
                }
                else
                {
                    // gfBits > 8, no multiplication table
                    int mulcPos = _gf_log[c];

                    // unroll your own damn loop.
                    for (; i < lim; i++, j++)
                    {
                        int y;

                        if ((y = src[j]) != 0)
                        {
                            dst[i] ^= _gf_exp[mulcPos + _gf_log[y]];
                        }
                    }
                }
            }

            public void MatMul(byte[] a, int aStart, byte[] b, int bStart, byte[] c, int cStart, int n, int k, int m)
            {
                for (int row = 0; row < n; row++)
                {
                    for (int col = 0; col < m; col++)
                    {
                        int posA = row * k;
                        int posB = col;
                        byte acc = (byte)0;

                        for (int i = 0; i < k; i++, posA++, posB += m)
                        {
                            acc ^= Mul(a[aStart + posA], b[bStart + posB]);
                        }

                        c[cStart + (row * m + col)] = acc;
                    }
                }
            }

            public void InvertMatrix(byte[] src, int k)
            {
                int[] indxc = new int[k];
                int[] indxr = new int[k];

                // ipiv marks elements already used as pivots.
                int[] ipiv = new int[k];

                byte[] id_row = CreateGFMatrix(1, k);
                //byte[] temp_row = CreateGFMatrix(1, k);

                for (int col = 0; col < k; col++)
                {
                    /*
                     * Zeroing column 'col', look for a non-zero element.
                     * First try on the diagonal, if it fails, look elsewhere.
                     */
                    int irow = -1;
                    int icol = -1;
                    bool foundPiv = false;

                    if (ipiv[col] != 1 && src[col * k + col] != 0)
                    {
                        irow = col;
                        icol = col;
                        foundPiv = true;
                    }

                    if (!foundPiv)
                    {
                    loop1:
                        for (int row = 0; row < k; row++)
                        {
                            if (ipiv[row] != 1)
                            {
                                for (int ix = 0; ix < k; ix++)
                                {
                                    if (ipiv[ix] == 0)
                                    {
                                        if (src[row * k + ix] != 0)
                                        {
                                            irow = row;
                                            icol = ix;
                                            foundPiv = true;
                                            goto loop1;
                                        }
                                    }
                                    else if (ipiv[ix] > 1)
                                    {
                                        throw new ArgumentException("singular matrix");
                                    }
                                }
                            }
                        }
                    }

                    // redundant??? I'm too lazy to figure it out -Justin
                    if (!foundPiv && icol == -1)
                    {
                        throw new ArgumentException("XXX pivot not found!");
                    }

                    // Ok, we've found a pivot by this point, so we can set the 
                    // foundPiv variable back to false.  The reason that this is
                    // so shittily laid out is that the original code had goto's :(
                    foundPiv = false;

                    ipiv[icol] = ipiv[icol] + 1;

                    /*
                     * swap rows irow and icol, so afterwards the diagonal
                     * element will be correct. Rarely done, not worth
                     * optimizing.
                     */
                    if (irow != icol)
                    {
                        for (int ix = 0; ix < k; ix++)
                        {
                            // swap 'em
                            byte tmp = src[irow * k + ix];
                            src[irow * k + ix] = src[icol * k + ix];
                            src[icol * k + ix] = tmp;
                        }
                    }

                    indxr[col] = irow;
                    indxc[col] = icol;

                    int pivotRowPos = icol * k;
                    byte c = src[pivotRowPos + icol];

                    if (c == 0)
                    {
                        throw new ArgumentException("singular matrix 2");
                    }

                    if (c != 1)
                    {
                        /* otherwhise this is a NOP */
                        /*
                         * this is done often , but optimizing is not so
                         * fruitful, at least in the obvious ways (unrolling)
                         */
                        c = _inverse[c];
                        src[pivotRowPos + icol] = (byte)1;

                        for (int ix = 0; ix < k; ix++)
                        {
                            src[pivotRowPos + ix] = Mul(c, src[pivotRowPos + ix]);
                        }
                    }

                    /*
                     * from all rows, remove multiples of the selected row
                     * to zero the relevant entry (in fact, the entry is not zero
                     * because we know it must be zero).
                     * (Here, if we know that the pivotRowPos is the identity,
                     * we can optimize the addMul).
                     */
                    id_row[icol] = (byte)1;

                    if (!Collection.Equals(src, pivotRowPos, id_row, 0, k))
                    {
                        for (int p = 0, ix = 0; ix < k; ix++, p += k)
                        {
                            if (ix != icol)
                            {
                                c = src[p + icol];
                                src[p + icol] = (byte)0;
                                AddMul(src, p, src, pivotRowPos, c, k);
                            }
                        }
                    }

                    id_row[icol] = (byte)0;
                } // done all columns

                for (int col = k - 1; col >= 0; col--)
                {
                    if (indxr[col] < 0 || indxr[col] >= k)
                    {
                        //System.err.println("AARGH, indxr[col] "+indxr[col]);
                    }
                    else if (indxc[col] < 0 || indxc[col] >= k)
                    {
                        //System.err.println("AARGH, indxc[col] "+indxc[col]);
                    }
                    else
                    {
                        if (indxr[col] != indxc[col])
                        {
                            for (int row = 0; row < k; row++)
                            {
                                // swap 'em
                                byte tmp = src[row * k + indxc[col]];
                                src[row * k + indxc[col]] = src[row * k + indxr[col]];
                                src[row * k + indxr[col]] = tmp;
                            }
                        }
                    }
                }
            }

            public void InvertVandermonde(byte[] src, int k)
            {
                if (k == 1)
                {
                    // degenerate case, matrix must be p^0 = 1
                    return;
                }

                /*
                 * c holds the coefficient of P(x) = Prod (x - p_i), i=0..k-1
                 * b holds the coefficient for the matrix inversion
                 */
                byte[] c = CreateGFMatrix(1, k);
                byte[] b = CreateGFMatrix(1, k);
                byte[] p = CreateGFMatrix(1, k);

                for (int j = 1, i = 0; i < k; i++, j += k)
                {
                    c[i] = (byte)0;
                    p[i] = src[j];    /* p[i] */
                }

                /*
                 * construct coeffs. recursively. We know c[k] = 1 (implicit)
                 * and start P_0 = x - p_0, then at each stage multiply by
                 * x - p_i generating P_i = x P_{i-1} - p_i P_{i-1}
                 * After k steps we are done.
                 */
                c[k - 1] = p[0];	/* really -p(0), but x = -x in GF(2^m) */

                for (int i = 1; i < k; i++)
                {
                    byte p_i = p[i]; /* see above comment */

                    for (int j = k - 1 - (i - 1); j < k - 1; j++)
                    {
                        c[j] ^= Mul(p_i, c[j + 1]);
                    }

                    c[k - 1] ^= p_i;
                }

                for (int row = 0; row < k; row++)
                {
                    /*
                     * synthetic division etc.
                     */
                    byte xx = p[row];
                    byte t = (byte)1;
                    b[k - 1] = (byte)1; /* this is in fact c[k] */

                    for (int i = k - 2; i >= 0; i--)
                    {
                        b[i] = (byte)(c[i + 1] ^ Mul(xx, b[i + 1]));
                        t = (byte)(Mul(xx, t) ^ b[i]);
                    }

                    for (int col = 0; col < k; col++)
                    {
                        src[col * k + row] = Mul(_inverse[t], b[col]);
                    }
                }
            }

            public byte[] CreateEncodeMatrix(int k, int n)
            {
                if (k > _gfSize + 1 || n > _gfSize + 1 || k > n)
                {
                    throw new ArgumentException("Invalid parameters n=" + n + ",k=" + k + ",gfSize=" + _gfSize);
                }

                byte[] encMatrix = CreateGFMatrix(n, k);

                /*
                 * The encoding matrix is computed starting with a Vandermonde matrix,
                 * and then transforming it into a systematic matrix.
                 *
                 * fill the matrix with powers of field elements, starting from 0.
                 * The first row is special, cannot be computed with exp. table.
                 */
                byte[] tmpMatrix = CreateGFMatrix(n, k);

                tmpMatrix[0] = (byte)1;

                // first row should be 0's, fill in the rest.
                for (int pos = k, row = 0; row < n - 1; row++, pos += k)
                {
                    for (int col = 0; col < k; col++)
                    {
                        tmpMatrix[pos + col] = _gf_exp[Modnn(row * col)];
                    }
                }

                /*
                 * quick code to build systematic matrix: invert the top
                 * k*k vandermonde matrix, multiply right the bottom n-k rows
                 * by the inverse, and construct the identity matrix at the top.
                 */
                // much faster than invertMatrix 
                InvertVandermonde(tmpMatrix, k);
                MatMul(tmpMatrix, k * k, tmpMatrix, 0, encMatrix, k * k, n - k, k, k);

                /*
                 * the upper matrix is I so do not bother with a slow multiply
                 */
                Array.Clear(encMatrix, 0, k * k);

                for (int i = 0, col = 0; col < k; col++, i += k + 1)
                {
                    encMatrix[i] = (byte)1;
                }

                return encMatrix;
            }

            public byte[] CreateDecodeMatrix(byte[] encMatrix, int[] index, int k, int n)
            {
                byte[] matrix = CreateGFMatrix(k, k);

                for (int i = 0, pos = 0; i < k; i++, pos += k)
                {
                    System.Array.Copy(encMatrix, index[i] * k, matrix, pos, k);
                }

                InvertMatrix(matrix, k);

                return matrix;
            }
        }
    }
#endif
}

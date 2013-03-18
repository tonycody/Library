#include <stdint.h>
#include <vector>
#include <string>
#include <iostream>
#include <fstream>

using namespace std;

class ReedSolomon
{
public:
	static class Math
	{
	private:
		int _gfBits;
		int _gfSize;
		std::vector<std::string> _prim_polys;
		uint8_t* _gf_exp;
		int* _gf_log;
		uint8_t* _inverse;
		uint8_t** _gf_mul_table;
	public:
		Math(){};
		Math(int gfBits){
            _gfBits = gfBits;
            _gfSize = ((1 << gfBits) - 1);

            _gf_exp = new uint8_t[2 * _gfSize];
            _gf_log = new int[_gfSize + 1];
            _inverse = new uint8_t[_gfSize + 1];

            if (gfBits < 2 || gfBits > 16)
            {
                throw 0xff;
            }
            _prim_polys.push_back("");                     // 0              no code
            _prim_polys.push_back("");                     // 1              no code
            _prim_polys.push_back("111");                    // 2              1+x+x^2
            _prim_polys.push_back("1101");	                  // 3              1+x+x^3
            _prim_polys.push_back("11001");       	          // 4              1+x+x^4
            _prim_polys.push_back("101001");      	          // 5              1+x^2+x^5
            _prim_polys.push_back("1100001");     	          // 6              1+x+x^6
            _prim_polys.push_back("10010001");    	          // 7              1+x^3+x^7
            _prim_polys.push_back("101110001");              // 8              1+x^2+x^3+x^4+x^8
            _prim_polys.push_back("1000100001");             // 9              1+x^4+x^9
            _prim_polys.push_back("10010000001");            // 10             1+x^3+x^10
            _prim_polys.push_back("101000000001");           // 11             1+x^2+x^11
            _prim_polys.push_back("1100101000001");          // 12             1+x+x^4+x^6+x^12
            _prim_polys.push_back("11011000000001");         // 13             1+x+x^3+x^4+x^13
            _prim_polys.push_back("110000100010001");        // 14             1+x+x^6+x^10+x^14
            _prim_polys.push_back("1100000000000001");       // 15             1+x+x^15
            _prim_polys.push_back("11010000000010001");       // 16             1+x+x^3+x^12+x^16
			GenerateGF();
            if (gfBits <= 8)
            {
                InitMulTable();
            }

		};
		void GenerateGF(){
			std::string primPoly = _prim_polys[_gfBits];
			uint8_t mask = (uint8_t)1;
			_gf_exp[_gfBits] = (uint8_t)0;
                for (int i = 0; i < _gfBits; i++, mask <<= 1)
                {
                    _gf_exp[i] = mask;
                    _gf_log[_gf_exp[i]] = i;

                    if (primPoly[i] == '1')
                    {
                        _gf_exp[_gfBits] ^= mask;
                    }
                }
                _gf_log[_gf_exp[_gfBits]] = _gfBits;

                mask = (uint8_t)(1 << (_gfBits - 1));

                for (int i = _gfBits + 1; i < _gfSize; i++)
                {
                    if (_gf_exp[i - 1] >= mask)
                    {
                        _gf_exp[i] = (uint8_t)(_gf_exp[_gfBits] ^ ((_gf_exp[i - 1] ^ mask) << 1));
                    }
                    else
                    {
                        _gf_exp[i] = (uint8_t)(_gf_exp[i - 1] << 1);
                    }
                    _gf_log[_gf_exp[i]] = i;
                }

                _gf_log[0] = _gfSize;

                for (int i = 0; i < _gfSize; i++)
                {
                    _gf_exp[i + _gfSize] = _gf_exp[i];
                }

                _inverse[0] = (uint8_t)0;
                _inverse[1] = (uint8_t)1;

                for (int i = 2; i <= _gfSize; i++)
                {
                    _inverse[i] = _gf_exp[_gfSize - _gf_log[i]];
                }
		};
		void InitMulTable(){
            if (_gfBits <= 8)
            {
				_gf_mul_table = new uint8_t*[_gfSize + 1];
				_gf_mul_table[0] = new uint8_t[(_gfSize + 1) * (_gfSize + 1)];

                for (int i = 0; i < _gfSize + 1; i++)
                {
                    _gf_mul_table[i] = new uint8_t[_gfSize + 1];
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
                    _gf_mul_table[0][i] = _gf_mul_table[i][0] = (uint8_t)0;
                }
            }
		};
		uint8_t Modnn(int x){
            while (x >= _gfSize)
            {
                x -= _gfSize;
                x = (x >> _gfBits) + (x & _gfSize);
            }

            return (uint8_t)x;
		};
		uint8_t Mul(uint8_t x, uint8_t y){
            if (_gfBits <= 8)
            {
                return _gf_mul_table[x][y];
            }
            else
            {
                if (x == 0 || y == 0)
                {
					return (uint8_t)0;
                }

                return _gf_exp[_gf_log[x] + _gf_log[y]];
            }
		};
		static uint8_t* CreateGFMatrix(int rows, int cols){
			return new uint8_t[rows * cols];
		};
		void AddMul(uint8_t* dst, int dstPos, uint8_t* src, int srcPos, uint8_t c, int len){
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
				uint8_t* gf_mulc = _gf_mul_table[c];

                for (; i < lim && (lim - i) > unroll; i += unroll, j += unroll)
                {
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
                for (; i < lim; i++, j++)
                {
                    dst[i] ^= gf_mulc[src[j]];
                }
            }
            else
            {
                int mulcPos = _gf_log[c];

                for (; i < lim; i++, j++)
                {
                    int y;

                    if ((y = src[j]) != 0)
                    {
                        dst[i] ^= _gf_exp[mulcPos + _gf_log[y]];
                    }
                }
            }
		};
		void MatMul(uint8_t* a, int aStart, uint8_t* b, int bStart, uint8_t* c, int cStart, int n, int k, int m){
            for (int row = 0; row < n; row++)
            {
                for (int col = 0; col < m; col++)
                {
                    int posA = row * k;
                    int posB = col;
					uint8_t acc = (uint8_t)0;

                    for (int i = 0; i < k; i++, posA++, posB += m)
                    {
                        acc ^= Mul(a[aStart + posA], b[bStart + posB]);
                    }

                    c[cStart + (row * m + col)] = acc;
                }
            }
		};
		void InvertMatrix(uint8_t* src, int k){
			int* indxc = new int[k];
			int* indxr = new int[k];

			int* ipiv = new int[k];
			for(int i=0;i<k;i++)
			{
				indxc[i] = 0;
				indxr[i] = 0;
				ipiv[i] = 0;
			}

			uint8_t* id_row = CreateGFMatrix(1, k);
			for(int i=0;i<k;i++)
			{
				id_row[i] = 0;
			}
			for (int col = 0; col < k; col++)
			{
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
									throw 0xff;
								}
							}
						}
					}
				}
				if (!foundPiv && icol == -1)
				{
					throw 0xff;
				}
				foundPiv = false;
				ipiv[icol] = ipiv[icol] + 1;
				if (irow != icol)
				{
					for (int ix = 0; ix < k; ix++)
					{
						uint8_t tmp = src[irow * k + ix];
						src[irow * k + ix] = src[icol * k + ix];
						src[icol * k + ix] = tmp;
					}
				}
				indxr[col] = irow;
				indxc[col] = icol;
				int pivotRowPos = icol * k;
				uint8_t c = src[pivotRowPos + icol];
				if (c == 0)
				{
					throw 0xff;
				}
				if (c != 1)
				{
					c = _inverse[c];
					src[pivotRowPos + icol] = uint8_t(1);
					for (int ix = 0; ix < k; ix++)
					{
						src[pivotRowPos + ix] = Mul(c, src[pivotRowPos + ix]);
					}
				}
				id_row[icol] = uint8_t(1);
				if (!Equals(src, pivotRowPos, id_row, 0, k))
				{
					for (int p = 0, ix = 0; ix < k; ix++, p += k)
					{
						if (ix != icol)
						{
							c = src[p + icol];
							src[p + icol] = uint8_t(0);
							AddMul(src, p, src, pivotRowPos, c, k);
						}
					}
				}
				id_row[icol] = uint8_t(0);
			}

			for (int col = k - 1; col >= 0; col--)
			{
				if (indxr[col] < 0 || indxr[col] >= k)
				{
				}
				else if (indxc[col] < 0 || indxc[col] >= k)
				{
				}
				else
				{
					if (indxr[col] != indxc[col])
					{
						for (int row = 0; row < k; row++)
						{
							uint8_t tmp = src[row * k + indxc[col]];
							src[row * k + indxc[col]] = src[row * k + indxr[col]];
							src[row * k + indxr[col]] = tmp;
						}
					}
				}
			}
		};
		void InvertVandermonde(uint8_t* src, int k){
                if (k == 1)
                {
                    return;
                }

                uint8_t* c = CreateGFMatrix(1, k);
                uint8_t* b = CreateGFMatrix(1, k);
                uint8_t* p = CreateGFMatrix(1, k);

                for (int j = 1, i = 0; i < k; i++, j += k)
                {
                    c[i] = (uint8_t)0;
                    p[i] = src[j];    /* p[i] */
                }

                c[k - 1] = p[0];	/* really -p(0), but x = -x in GF(2^m) */

                for (int i = 1; i < k; i++)
                {
                    uint8_t p_i = p[i]; /* see above comment */

                    for (int j = k - 1 - (i - 1); j < k - 1; j++)
                    {
                        c[j] ^= Mul(p_i, c[j + 1]);
                    }

                    c[k - 1] ^= p_i;
                }

                for (int row = 0; row < k; row++)
                {
                    uint8_t xx = p[row];
                    uint8_t t = (uint8_t)1;
                    b[k - 1] = (uint8_t)1; /* this is in fact c[k] */

                    for (int i = k - 2; i >= 0; i--)
                    {
                        b[i] = (uint8_t)(c[i + 1] ^ Mul(xx, b[i + 1]));
                        t = (uint8_t)(Mul(xx, t) ^ b[i]);
                    }

                    for (int col = 0; col < k; col++)
                    {
                        src[col * k + row] = Mul(_inverse[t], b[col]);
                    }
                }
		};
		uint8_t* CreateEncodeMatrix(int k, int n){

                if (k > _gfSize + 1 || n > _gfSize + 1 || k > n)
                {
                    throw 0xff;
                }

                uint8_t* encMatrix = CreateGFMatrix(n, k);

                uint8_t* tmpMatrix = CreateGFMatrix(n, k);
				for(int i=0; i < n * k; i++)
				{
					tmpMatrix[i] = (uint8_t)0;
				}

                tmpMatrix[0] = (uint8_t)1;

                for (int pos = k, row = 0; row < n - 1; row++, pos += k)
                {
                    for (int col = 0; col < k; col++)
                    {
                        tmpMatrix[pos + col] = _gf_exp[Modnn(row * col)];
                    }
                }
                InvertVandermonde(tmpMatrix, k);
                MatMul(tmpMatrix, k * k, tmpMatrix, 0, encMatrix, k * k, n - k, k, k);

				for(int i=0; i < k * k; i++)
				{
					encMatrix[i] = (uint8_t)0;
				}

                for (int i = 0, col = 0; col < k; col++, i += k + 1)
                {
                    encMatrix[i] = (uint8_t)1;
                }

                return encMatrix;
		};
		uint8_t* CreateDecodeMatrix(uint8_t* encMatrix, int* index, int k, int n){
            uint8_t* matrix = CreateGFMatrix(k, k);
			for(int i=0;i<k*k;i++)
			{
				matrix[i] = 0;
			}
            for (int i = 0, pos = 0; i < k; i++, pos += k)
            {
				for(int j=0; j<k; j++)
				{
					matrix[pos + j] = encMatrix[j + index[i] * k];
				}
            }
			
            InvertMatrix(matrix, k);

            return matrix;
		};

		static bool Equals(uint8_t* sourceCollection, int sourceIndex, uint8_t* destinationCollection, int destinationIndex, int length){
            for (int i = sourceIndex, j = destinationIndex, k = 0; k < length; i++, j++, k++)
            {
                if (sourceCollection[i] != destinationCollection[j])
                {
                    return false;
                }
            }

            return true;
		};
	};
private:
	int32_t _k;
	int32_t _n;
	int32_t _threadCount;
	uint8_t* _encMatrix;
	ReedSolomon::Math _fecMath;
	static void CopyShuffle(uint8_t** pkts, int32_t* index, int k){
        uint8_t* buffer = NULL;

        for (int i = 0; i < k; )
        {
            if (index[i] >= k || index[i] == i)
            {
                i++;
            }
            else
            {
                int c = index[i];

                if (index[c] == c)
                {
                    throw 0xff;
                }

                int tmp = index[i];
                index[i] = index[c];
                index[c] = tmp;

                if (buffer == NULL)
                {
					buffer = new uint8_t[sizeof(pkts[0])];
                }
				for(int j=0;j<sizeof(pkts[0]);j++)
				{
					buffer[j] = pkts[i][j];
					pkts[i][j] =  pkts[c][j];
					pkts[c][j] = buffer[j];
				}
            }
        }
	};
public:
	ReedSolomon(){};
	ReedSolomon(int gfBits, int k, int n, int threadCount){
		_fecMath = ReedSolomon::Math(gfBits);
		_k = k;
		_n = n;
		_threadCount = threadCount;
		_encMatrix = _fecMath.CreateEncodeMatrix(k,n);
	};
	void Encode(uint8_t** src, int32_t* srcOffset, uint8_t** repair, int32_t* repairOffset, int32_t* index){
        Encode(src, srcOffset, repair, repairOffset, index, sizeof(src[0]));
	};
	void Decode(uint8_t** pkts, int32_t* pktsOffset, int* index){
		CopyShuffle(pkts, index, _k);
		Decode(pkts, pktsOffset, index, sizeof(pkts[0]));
	};
private:
	void Encode(uint8_t** src, int32_t* srcOff, uint8_t **repair, int32_t* repairOff, int32_t* index, int packetLength){
		for(int row=0;row<_n;row++)
		{
            if (index[row] < _k)
            {
				for(int j=0;j<packetLength;j++)
				{
					repair[row][j+repairOff[row]] = src[index[row]][j+srcOff[index[row]]];
				}
            }
            else
            {
                int pos = index[row] * _k;
				for(int j=0;j<packetLength;j++)
				{
					repair[row][j+repairOff[row]] = (uint8_t)0;
				}

                for (int col = 0; col < _k; col++)
                {
                    _fecMath.AddMul(repair[row], repairOff[row], src[col], srcOff[col], (uint8_t)_encMatrix[pos + col], packetLength);
                }
            }
		}
	};
	void Decode(uint8_t** pkts, int32_t* pktsOff, int32_t* index, int packetLength){
        uint8_t* decMatrix = _fecMath.CreateDecodeMatrix(_encMatrix, index, _k, _n);

        uint8_t** tmpPkts = new uint8_t*[_k];
		tmpPkts[0] = new uint8_t[_k*packetLength];

        for (int row = 0; row < _k; row++)
        {
            if (index[row] >= _k)
            {
				tmpPkts[row] = new uint8_t[packetLength];
				for(int j=0;j<packetLength;j++)
				{
					tmpPkts[row][j] = 0;
				}

                for (int col = 0; col < _k; col++)
                {
                    _fecMath.AddMul(tmpPkts[row], 0, pkts[col], pktsOff[col], (uint8_t)decMatrix[row * _k + col], packetLength);
                }
            }
        }

        for (int row = 0; row < _k; row++)
        {
            if (index[row] >= _k)
            {
				for(int j=0;j<packetLength;j++)
				{
					pkts[row][pktsOff[row]+j] = tmpPkts[row][j];
				}
                index[row] = row;
            }
        }
	};
};
static uint8_t* GetBytes(int value){};
int main()
{
	ReedSolomon *pc = new ReedSolomon(8, 128, 256, 4);
	
	uint8_t** BuffList = new uint8_t*[128];
	BuffList[0] = new uint8_t[128*4];
	for(int i=0;i<128;i++)
	{
		BuffList[i] = new uint8_t[4];	
		const uint8_t* u = (uint8_t*)&(i);
		std::cout<<sizeof(BuffList[i])<<std::endl;
		std::cout<<sizeof(u)<<std::endl;
		for(int j=0;j<4;j++)
		{
			BuffList[i][j] = u[3-j];
		}
	}
	int32_t* BuffList_Offset = new int32_t[128];
	for(int i=0;i<128;i++)
	{
		BuffList_Offset[i] = 0;
	}

	uint8_t** BuffList2 = new uint8_t*[256];
	BuffList2[0] = new uint8_t[256*4];
	for(int i=0;i<256;i++)
	{
		BuffList2[i] = new uint8_t[4];
		for(int j=0;j<4;j++)
		{
			BuffList2[i][j] = 0;
		}
	}

	int32_t* BuffList2_Offset = new int32_t[256];
	for(int i=0;i<256;i++)
	{
		BuffList2_Offset[i] = 0;
	}
	int32_t* intList = new int32_t[256];
	for(int i=0;i<256;i++)
	{
		intList[i] = i;
	}

	pc->Encode(BuffList, BuffList_Offset, BuffList2, BuffList2_Offset, intList);

	uint8_t** BuffList3 = new uint8_t*[128];
	BuffList3[0] = new uint8_t[128*4];
	int32_t* BuffList3_Offset = new int32_t[128];
	for(int i=0;i<64;i++)
	{
		BuffList3[i] = new uint8_t[4];
		BuffList3[i] = BuffList2[i];
		BuffList3_Offset[i] = BuffList2_Offset[i];
	}
	for(int i=0;i<64;i++)
	{
		BuffList3[64+i] = new uint8_t[4];
		BuffList3[64+i] = BuffList2[128+i];
		BuffList3_Offset[64+i] = BuffList2_Offset[128+i];
	}
	int32_t* intList2 = new int32_t[128];
	for(int i=0;i<64;i++)
	{
		intList2[i] = i;
	}
	for(int i=0;i<64;i++)
	{
		intList2[64+i] = 128+i;
	}
	pc->Decode(BuffList3, BuffList3_Offset, intList2);

	for(int i=0;i<128;i++)
	{
		for(int j=0;j<4;j++)
		{
			std::cout<<BuffList[i][j]<<" "<<BuffList3[i][j]<<" ";
		}
		std::cout<<std::endl;
	}
	{
		std::ofstream ofs("cpp.txt");
		for(int i=0;i<128;i++)
		{
			for(int j=0;j<4;j++)
			{
				ofs<<(int)BuffList[i][j]<<" ";
			}
		}
		ofs<<std::endl;
		for(int i=0;i<128;i++)
		{
			ofs<<BuffList_Offset[i]<<" ";
		}
		ofs<<std::endl;
		for(int i=0;i<256;i++)
		{
			for(int j=0;j<4;j++)
			{
				ofs<<(int)BuffList2[i][j]<<" ";
			}
		}
		ofs<<std::endl;
		for(int i=0;i<256;i++)
		{
			ofs<<BuffList2_Offset[i]<<" ";
		}
		ofs<<std::endl;
		for(int i=0;i<128;i++)
		{
			for(int j=0;j<4;j++)
			{
				ofs<<(int)BuffList3[i][j]<<" ";
			}
		}
		ofs<<std::endl;
		for(int i=0;i<128;i++)
		{
			ofs<<BuffList3_Offset[i]<<" ";
		}
		ofs<<std::endl;
	}
	return 0;
}
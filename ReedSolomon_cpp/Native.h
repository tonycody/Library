#include <stdint.h>
#include <vector>

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
		Math();
		Math(int gfBits);
		void GenerateGF();
		void InitMulTable();
		uint8_t Modnn(int x);
		uint8_t Mul(uint8_t x, uint8_t y);
		static uint8_t* CreateGFMatrix(int rows, int cols);
		void AddMul(uint8_t* dst, int dstPos, uint8_t* src, int srcPos, uint8_t c, int len);
		void MatMul(uint8_t* a, int aStart, uint8_t* b, int bStart, uint8_t* c, int cStart, int n, int k, int m);
		void InvertMatrix(uint8_t* src, int k);
		void InvertVandermonde(uint8_t* src, int k);
		uint8_t* CreateEncodeMatrix(int k, int n);
		uint8_t* CreateDecodeMatrix(uint8_t* encMatrix, int* index, int k, int n);

		static bool Equals(uint8_t* sourceCollection, int sourceIndex, uint8_t* destinationCollection, int destinationIndex, int length);
	};
private:
	int32_t _k;
	int32_t _n;
	int32_t _threadCount;
	uint8_t* _encMatrix;
	ReedSolomon::Math _fecMath;
	static void CopyShuffle(uint8_t** pkts, int32_t* index, int k);
public:
	ReedSolomon();
	ReedSolomon(int gfBits, int k, int n, int threadCount);
	void Encode(uint8_t** src, int32_t* srcOffset, uint8_t** repair, int32_t* repairOffset, int32_t* index);
	void Decode(uint8_t** pkts, int32_t* pktsOffset, int* index);
private:
	void Encode(uint8_t** src, int32_t* srcOff, uint8_t **repair, int32_t* repairOff, int32_t* index, int packetLength);
	void Decode(uint8_t** pkts, int32_t* pktsOff, int32_t* index, int packetLength);
};

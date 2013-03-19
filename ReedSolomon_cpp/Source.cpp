#include "Native.h"
#include <fstream>
#include <iostream>
#include <time.h>
using namespace std;

int main()
{
	ReedSolomon *pc = new ReedSolomon(8, 128, 256, 4);
	
	uint8_t** BuffList = new uint8_t*[128];
	BuffList[0] = new uint8_t[128*4];
	for(int i=0;i<128*8;i+=8)
	{
		BuffList[i/8] = new uint8_t[4];	
		const uint8_t* u = (uint8_t*)&(i);
		for(int j=0;j<4;j++)
		{
			BuffList[i/8][j] = u[3-j];
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
	clock_t start,end;
	start = clock();
	for(int i=0;i<1000*10;i++)
	{
		pc->Encode(BuffList, BuffList_Offset, BuffList2, BuffList2_Offset, intList);
	}
	end = clock();
	std::cout<<(double)(end-start)/CLOCKS_PER_SEC<<std::endl;

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
	system("pause");
	return 0;
}
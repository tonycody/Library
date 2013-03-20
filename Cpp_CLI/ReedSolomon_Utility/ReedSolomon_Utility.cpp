// This is the main DLL file.

#include "stdafx.h"

#include "ReedSolomon_Utility.h"
#include "fec.h"

#pragma comment(lib,"ReedSolomon8.lib")

#using <mscorlib.dll>

using namespace System;
		
namespace ReedSolomon_Utility
{
    public ref class FEC
	{
	private:
		struct fec_parms *_fec_parms;
		int _k;
		int _n;

		static FEC()
		{
			init_fec();
		}
		
	public:
		FEC(int k, int n)
		{
			_k = k;
			_n = n;
			_fec_parms = fec_new(k, n);
		}	

        ~FEC()
        {
            fec_free(_fec_parms);
        }

		void Encode(array<array<Byte>^>^ src, array<array<Byte>^>^ repair, array<int>^ index, int size)
        {
			Byte **tsrc = new Byte*[src->Length];
			
			for(int i = 0; i < src->Length; i++)
			{
				pin_ptr<Byte> p = &src[i][0];
				unsigned char * cp = p;
				tsrc[i] = cp;
			}
			
			for(int i = 0; i< index->Length; i++)
			{
				pin_ptr<Byte> pr = &repair[i][0];

				fec_encode(_fec_parms, tsrc, pr, index[i], size);
			}

			delete [] tsrc;
        }

		bool Decode(array<array<Byte>^>^ pkts, array<int>^ index, int size)
		{
			Byte **tpkts = new Byte*[pkts->Length];
			
			for(int i = 0; i < pkts->Length; i++)
			{
				pin_ptr<Byte> p = &pkts[i][0];
				unsigned char * cp = p;
				tpkts[i] = cp;
			}
			
			pin_ptr<int> ip = &index[0];
			
            int f = fec_decode(_fec_parms, tpkts, ip, size);

			delete [] tpkts;

            return (f == 0);
		}
	};
}

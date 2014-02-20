// This is the main DLL file.

#include "stdafx.h"

#include "ReedSolomon.h"
#include "fec.h"

#pragma comment(lib,"ReedSolomon8.lib")

#using <mscorlib.dll>

using namespace System;
		
namespace ReedSolomon
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
            set(0);
		}	

        ~FEC()
        {
            this->!FEC();
        }

        !FEC()
        {
            fec_free(_fec_parms);
        }

        void Encode(array<array<Byte>^>^ src, array<array<Byte>^>^ repair, array<int>^ index, int size)
        {
            set(0);

            Byte **tsrc = new Byte*[src->Length];
			
			for(int i = 0; i < src->Length; i++)
			{
				pin_ptr<Byte> p = &src[i][0];
				unsigned char * cp = p;
				tsrc[i] = cp;
			}
			
			for(int i = 0; i< index->Length; i++)
			{ 
                if(get() == 1) goto End;

				pin_ptr<Byte> pr = &repair[i][0];

				fec_encode(_fec_parms, tsrc, pr, index[i], size);
			}

			for(int i = 0; i < src->Length; i++)
			{
                for (int j = 0; j < src->Length; j++)
                {
                    if(tsrc[i] == &src[j][0])
                    {
                        array<Byte>^ temp = src[i];
                        src[i] = src[j];
                        src[j] = temp;

                        break;
                    }
                }
            }

End:;

            delete [] tsrc;
        }

        void Decode(array<array<Byte>^>^ pkts, array<int>^ index, int size)
		{
            set(0);

            Byte **tpkts = new Byte*[pkts->Length];
			
			for(int i = 0; i < pkts->Length; i++)
			{
				pin_ptr<Byte> p = &pkts[i][0];
				unsigned char * cp = p;
				tpkts[i] = cp;
			}
			
			pin_ptr<int> ip = &index[0];

            fec_decode(_fec_parms, tpkts, ip, size);

			if(get() == 1) goto End;

            for(int i = 0; i < pkts->Length; i++)
			{
                for (int j = pkts->Length - 1; j >= 0 ; j--)
                {
                    if(tpkts[i] == &pkts[j][0])
                    {
                        array<Byte>^ temp = pkts[i];
                        pkts[i] = pkts[j];
                        pkts[j] = temp;
                  
                        break;
                    }
                }
            }

End:;

			delete [] tpkts;
		}
        
        void Cancel()
        {
            set(1);
        }
	};
}

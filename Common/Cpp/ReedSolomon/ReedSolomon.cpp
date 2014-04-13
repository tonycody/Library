// This is the main DLL file.

#include "stdafx.h"
#include "ReedSolomon.h"

#using <mscorlib.dll>

using namespace System;
using namespace System::Collections::Generic;
using namespace System::Runtime::InteropServices;

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
		
        static void Shuffle(array<array<Byte>^>^ pkts, array<int>^ index, int k)
        {
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
                        throw "Shuffle error at " + i;
                    }

                    // swap(index[c],index[i])
                    int tmp = index[i];
                    index[i] = index[c];
                    index[c] = tmp;

                    // swap(pkts[c],pkts[i])
                    array<Byte>^ tmp2 = pkts[i];
                    pkts[i] = pkts[c];
                    pkts[c] = tmp2;
                }
            }
        }

    public:
		FEC(int k, int n)
		{
			_k = k;
			_n = n;
			_fec_parms = fec_new(k, n);
            setFlag(0);
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
            setFlag(0);

            List<GCHandle>^ hsrc = gcnew List<GCHandle>();

            for(int i = 0; i < src->Length; i++)
			{
                hsrc->Add(GCHandle::Alloc(src[i], System::Runtime::InteropServices::GCHandleType::Pinned));
            }

            Byte **tsrc = new Byte*[src->Length];
			
			for(int i = 0; i < src->Length; i++)
			{
                tsrc[i] = (Byte*)(void*)hsrc[i].AddrOfPinnedObject();
			}
			
			for(int i = 0; i< index->Length; i++)
			{ 
                if(getFlag() == 1) goto End;

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
            
            for each (GCHandle item in hsrc)
            {
                item.Free();
            }
            hsrc->Clear();

            delete [] tsrc;
        }

        void Decode(array<array<Byte>^>^ pkts, array<int>^ index, int size)
		{
            setFlag(0);

            Shuffle(pkts, index, _k);

            List<GCHandle>^ hpkts = gcnew List<GCHandle>();

            for(int i = 0; i < pkts->Length; i++)
			{
                hpkts->Add(GCHandle::Alloc(pkts[i], System::Runtime::InteropServices::GCHandleType::Pinned));
            }

            Byte **tpkts = new Byte*[pkts->Length];
			
			for(int i = 0; i < pkts->Length; i++)
			{
                tpkts[i] = (Byte*)(void*)hpkts[i].AddrOfPinnedObject();
			}
			
			pin_ptr<int> ip = &index[0];

            fec_decode(_fec_parms, tpkts, ip, size);

End:;

            for each (GCHandle item in hpkts)
            {
                item.Free();
            }
            hpkts->Clear();

			delete [] tpkts;
		}
        
        void Cancel()
        {
            setFlag(1);
        }
	};
}

// This is the main DLL file.

#include "stdafx.h"

#include "ReedSolomon.h"
#include "fec.h"

#pragma comment(lib,"ReedSolomon8.lib")

#using <mscorlib.dll>

using namespace System;
		
namespace ReedSolomon {

	public ref class Math
	{
	private:
		struct fec_parms *_fec_parms;
		int _k;
		int _n;

		static Math()
		{
			init_fec();
		}
		
	public:
		Math(int k, int n)
		{
			_k = k;
			_n = n;
			_fec_parms = fec_new(k, n);
		}	

		void Encode(array<array<Byte>^>^ src, array<array<Byte>^>^ repair, array<int>^ index, int size)
        {
		   //for( i = 0 ; i < k ; i++ )
		   //{
			  // fec_encode(code, d_original, d_src[i], index[i], sz );
		   //}
			//Byte *stList[src.Length];

			//pin_ptr<Byte>[] sl = new pin_ptr<Byte>[src.Length];
			array<pin_ptr<Byte>>^ array1D = gcnew array<pin_ptr<Byte>>();
			pin_ptr<Byte> *slist = new pin_ptr<Byte>[src->Length];
			
			for(int i = 0; i < src->Length; i++)
			{
				//pin_ptr<Byte> pr = &src[i][0];
				*gf[i] = &src[i][0];
			}
			
			for(int i = 0; i< _k; i++)
			{
				//pin_ptr<*Byte> ps = &src[0];
				pin_ptr<Byte> pr = &repair[i][0];

				//fec_encode(_fec_parms,&src,&pr,index[i],size);
			}
        }
	};
}

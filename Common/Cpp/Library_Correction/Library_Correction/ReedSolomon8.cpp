#include "stdafx.h"
#include "ReedSolomon8.h"

// 32bit Test
//#define PORTABLE_32_BIT_TEST

#ifndef PORTABLE_32_BIT_TEST
    #if  _WIN64 || __amd64__
    #define PORTABLE_64_BIT
    #else
    #define PORTABLE_32_BIT
    #endif
#else
    #define PORTABLE_32_BIT
#endif

#include "xmmintrin.h" //SSE
#include "emmintrin.h" //SSE2
//#include "pmmintrin.h" //SSE3
//#include "tmmintrin.h" //SSSE3
//#include "smmintrin.h" //SSE4.1
//#include "nmmintrin.h" //SSE4.2
//#include "wmmintrin.h" //AES
//#include "immintrin.h" //AVX

void mul(byte* src, byte* dst, byte* mulc, int32_t len)
{
    __m128i xmm0;
    __m128i xmm1;
    __m128i xmm2;
    __m128i xmm3;
    __m128i xmm4; 
    __m128i xmm5;
    __m128i xmm6;
    __m128i xmm7;

    int32_t i = 0;
        
    // アライメントを揃える。
    for( ; i < len; i++)
    {
        if(((uintptr_t)dst % 16) == 0) break;

        *dst++ ^= mulc[*src++];
    }

    {
        _mm_prefetch((char*)mulc, _MM_HINT_NTA);
        _mm_prefetch((char*)mulc + 32, _MM_HINT_NTA);
        _mm_prefetch((char*)mulc + 64, _MM_HINT_NTA);
        _mm_prefetch((char*)mulc + 96, _MM_HINT_NTA);
        _mm_prefetch((char*)mulc + 128, _MM_HINT_NTA);
        _mm_prefetch((char*)mulc + 160, _MM_HINT_NTA);   
        _mm_prefetch((char*)mulc + 192, _MM_HINT_NTA);   
        _mm_prefetch((char*)mulc + 224, _MM_HINT_NTA);   
    }

    for (int32_t count = ((len - i) / 128) - 1; count >= 0 ; count--)
    {
        xmm0.m128i_u8[0] = mulc[*src++];
        xmm0.m128i_u8[1] = mulc[*src++];
        xmm0.m128i_u8[2] = mulc[*src++];
        xmm0.m128i_u8[3] = mulc[*src++];
        xmm0.m128i_u8[4] = mulc[*src++];
        xmm0.m128i_u8[5] = mulc[*src++];
        xmm0.m128i_u8[6] = mulc[*src++];
        xmm0.m128i_u8[7] = mulc[*src++];
        xmm0.m128i_u8[8] = mulc[*src++];
        xmm0.m128i_u8[9] = mulc[*src++];
        xmm0.m128i_u8[10] = mulc[*src++];
        xmm0.m128i_u8[11] = mulc[*src++];
        xmm0.m128i_u8[12] = mulc[*src++];
        xmm0.m128i_u8[13] = mulc[*src++];
        xmm0.m128i_u8[14] = mulc[*src++];
        xmm0.m128i_u8[15] = mulc[*src++];

        xmm1.m128i_u8[0] = mulc[*src++];
        xmm1.m128i_u8[1] = mulc[*src++];
        xmm1.m128i_u8[2] = mulc[*src++];
        xmm1.m128i_u8[3] = mulc[*src++];
        xmm1.m128i_u8[4] = mulc[*src++];
        xmm1.m128i_u8[5] = mulc[*src++];
        xmm1.m128i_u8[6] = mulc[*src++];
        xmm1.m128i_u8[7] = mulc[*src++];
        xmm1.m128i_u8[8] = mulc[*src++];
        xmm1.m128i_u8[9] = mulc[*src++];
        xmm1.m128i_u8[10] = mulc[*src++];
        xmm1.m128i_u8[11] = mulc[*src++];
        xmm1.m128i_u8[12] = mulc[*src++];
        xmm1.m128i_u8[13] = mulc[*src++];
        xmm1.m128i_u8[14] = mulc[*src++];
        xmm1.m128i_u8[15] = mulc[*src++];

        xmm2.m128i_u8[0] = mulc[*src++];
        xmm2.m128i_u8[1] = mulc[*src++];
        xmm2.m128i_u8[2] = mulc[*src++];
        xmm2.m128i_u8[3] = mulc[*src++];
        xmm2.m128i_u8[4] = mulc[*src++];
        xmm2.m128i_u8[5] = mulc[*src++];
        xmm2.m128i_u8[6] = mulc[*src++];
        xmm2.m128i_u8[7] = mulc[*src++];
        xmm2.m128i_u8[8] = mulc[*src++];
        xmm2.m128i_u8[9] = mulc[*src++];
        xmm2.m128i_u8[10] = mulc[*src++];
        xmm2.m128i_u8[11] = mulc[*src++];
        xmm2.m128i_u8[12] = mulc[*src++];
        xmm2.m128i_u8[13] = mulc[*src++];
        xmm2.m128i_u8[14] = mulc[*src++];
        xmm2.m128i_u8[15] = mulc[*src++];

        xmm3.m128i_u8[0] = mulc[*src++];
        xmm3.m128i_u8[1] = mulc[*src++];
        xmm3.m128i_u8[2] = mulc[*src++];
        xmm3.m128i_u8[3] = mulc[*src++];
        xmm3.m128i_u8[4] = mulc[*src++];
        xmm3.m128i_u8[5] = mulc[*src++];
        xmm3.m128i_u8[6] = mulc[*src++];
        xmm3.m128i_u8[7] = mulc[*src++];
        xmm3.m128i_u8[8] = mulc[*src++];
        xmm3.m128i_u8[9] = mulc[*src++];
        xmm3.m128i_u8[10] = mulc[*src++];
        xmm3.m128i_u8[11] = mulc[*src++];
        xmm3.m128i_u8[12] = mulc[*src++];
        xmm3.m128i_u8[13] = mulc[*src++];
        xmm3.m128i_u8[14] = mulc[*src++];
        xmm3.m128i_u8[15] = mulc[*src++];
       
        xmm4.m128i_u8[0] = mulc[*src++];
        xmm4.m128i_u8[1] = mulc[*src++];
        xmm4.m128i_u8[2] = mulc[*src++];
        xmm4.m128i_u8[3] = mulc[*src++];
        xmm4.m128i_u8[4] = mulc[*src++];
        xmm4.m128i_u8[5] = mulc[*src++];
        xmm4.m128i_u8[6] = mulc[*src++];
        xmm4.m128i_u8[7] = mulc[*src++];
        xmm4.m128i_u8[8] = mulc[*src++];
        xmm4.m128i_u8[9] = mulc[*src++];
        xmm4.m128i_u8[10] = mulc[*src++];
        xmm4.m128i_u8[11] = mulc[*src++];
        xmm4.m128i_u8[12] = mulc[*src++];
        xmm4.m128i_u8[13] = mulc[*src++];
        xmm4.m128i_u8[14] = mulc[*src++];
        xmm4.m128i_u8[15] = mulc[*src++];
       
        xmm5.m128i_u8[0] = mulc[*src++];
        xmm5.m128i_u8[1] = mulc[*src++];
        xmm5.m128i_u8[2] = mulc[*src++];
        xmm5.m128i_u8[3] = mulc[*src++];
        xmm5.m128i_u8[4] = mulc[*src++];
        xmm5.m128i_u8[5] = mulc[*src++];
        xmm5.m128i_u8[6] = mulc[*src++];
        xmm5.m128i_u8[7] = mulc[*src++];
        xmm5.m128i_u8[8] = mulc[*src++];
        xmm5.m128i_u8[9] = mulc[*src++];
        xmm5.m128i_u8[10] = mulc[*src++];
        xmm5.m128i_u8[11] = mulc[*src++];
        xmm5.m128i_u8[12] = mulc[*src++];
        xmm5.m128i_u8[13] = mulc[*src++];
        xmm5.m128i_u8[14] = mulc[*src++];
        xmm5.m128i_u8[15] = mulc[*src++];
       
        xmm6.m128i_u8[0] = mulc[*src++];
        xmm6.m128i_u8[1] = mulc[*src++];
        xmm6.m128i_u8[2] = mulc[*src++];
        xmm6.m128i_u8[3] = mulc[*src++];
        xmm6.m128i_u8[4] = mulc[*src++];
        xmm6.m128i_u8[5] = mulc[*src++];
        xmm6.m128i_u8[6] = mulc[*src++];
        xmm6.m128i_u8[7] = mulc[*src++];
        xmm6.m128i_u8[8] = mulc[*src++];
        xmm6.m128i_u8[9] = mulc[*src++];
        xmm6.m128i_u8[10] = mulc[*src++];
        xmm6.m128i_u8[11] = mulc[*src++];
        xmm6.m128i_u8[12] = mulc[*src++];
        xmm6.m128i_u8[13] = mulc[*src++];
        xmm6.m128i_u8[14] = mulc[*src++];
        xmm6.m128i_u8[15] = mulc[*src++];
       
        xmm7.m128i_u8[0] = mulc[*src++];
        xmm7.m128i_u8[1] = mulc[*src++];
        xmm7.m128i_u8[2] = mulc[*src++];
        xmm7.m128i_u8[3] = mulc[*src++];
        xmm7.m128i_u8[4] = mulc[*src++];
        xmm7.m128i_u8[5] = mulc[*src++];
        xmm7.m128i_u8[6] = mulc[*src++];
        xmm7.m128i_u8[7] = mulc[*src++];
        xmm7.m128i_u8[8] = mulc[*src++];
        xmm7.m128i_u8[9] = mulc[*src++];
        xmm7.m128i_u8[10] = mulc[*src++];
        xmm7.m128i_u8[11] = mulc[*src++];
        xmm7.m128i_u8[12] = mulc[*src++];
        xmm7.m128i_u8[13] = mulc[*src++];
        xmm7.m128i_u8[14] = mulc[*src++];
        xmm7.m128i_u8[15] = mulc[*src++];
       
        _mm_store_si128((__m128i*)dst, _mm_xor_si128(xmm0, _mm_load_si128((__m128i*)dst)));
        _mm_store_si128((__m128i*)(dst + 16), _mm_xor_si128(xmm1, _mm_load_si128((__m128i*)(dst + 16))));
        _mm_store_si128((__m128i*)(dst + (16 * 2)), _mm_xor_si128(xmm2, _mm_load_si128((__m128i*)(dst + (16 * 2)))));
        _mm_store_si128((__m128i*)(dst + (16 * 3)), _mm_xor_si128(xmm3, _mm_load_si128((__m128i*)(dst + (16 * 3)))));
        _mm_store_si128((__m128i*)(dst + (16 * 4)), _mm_xor_si128(xmm4, _mm_load_si128((__m128i*)(dst + (16 * 4)))));
        _mm_store_si128((__m128i*)(dst + (16 * 5)), _mm_xor_si128(xmm5, _mm_load_si128((__m128i*)(dst + (16 * 5)))));
        _mm_store_si128((__m128i*)(dst + (16 * 6)), _mm_xor_si128(xmm6, _mm_load_si128((__m128i*)(dst + (16 * 6)))));
        _mm_store_si128((__m128i*)(dst + (16 * 7)), _mm_xor_si128(xmm7, _mm_load_si128((__m128i*)(dst + (16 * 7)))));

        dst += 128;
        i += 128;
    }

    for( ; i < len; i++)
    {
        *dst++ ^= mulc[*src++];
    }
}

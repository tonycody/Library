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
#if defined (PORTABLE_64_BIT)
    __m128i xmm0;
    __m128i xmm1;
    __m128i xmm2;
    __m128i xmm3;
    __m128i xmm4;
    __m128i xmm5;
    __m128i xmm6;
    __m128i xmm7;
    __m128i xmm8; 
    __m128i xmm9;
    __m128i xmm10;
    __m128i xmm11;
    __m128i xmm12;
    __m128i xmm13;
    __m128i xmm14;
    __m128i xmm15;

    int32_t i = 0;
        
    // アライメントを揃える。
    for( ; i < len; i++)
    {
        if(((uintptr_t)dst % 32) == 0) break;

        *dst++ ^= mulc[*src++];
    }

    for (int32_t count = ((len - i) / 128) - 1; count >= 0; count--)
    {
        xmm0 = _mm_setr_epi8
        (
            mulc[*(src + 0)],
            mulc[*(src + 1)],
            mulc[*(src + 2)],
            mulc[*(src + 3)],

            mulc[*(src + 4)],
            mulc[*(src + 5)],
            mulc[*(src + 6)],
            mulc[*(src + 7)],

            mulc[*(src + 8)],
            mulc[*(src + 9)],
            mulc[*(src + 10)],
            mulc[*(src + 11)],

            mulc[*(src + 12)],
            mulc[*(src + 13)],
            mulc[*(src + 14)],
            mulc[*(src + 15)]
        );
        src += 16;

        xmm1 = _mm_setr_epi8
        (
            mulc[*(src + 0)],
            mulc[*(src + 1)],
            mulc[*(src + 2)],
            mulc[*(src + 3)],

            mulc[*(src + 4)],
            mulc[*(src + 5)],
            mulc[*(src + 6)],
            mulc[*(src + 7)],

            mulc[*(src + 8)],
            mulc[*(src + 9)],
            mulc[*(src + 10)],
            mulc[*(src + 11)],

            mulc[*(src + 12)],
            mulc[*(src + 13)],
            mulc[*(src + 14)],
            mulc[*(src + 15)]
        );
        src += 16;
       
        xmm2 = _mm_setr_epi8
        (
            mulc[*(src + 0)],
            mulc[*(src + 1)],
            mulc[*(src + 2)],
            mulc[*(src + 3)],

            mulc[*(src + 4)],
            mulc[*(src + 5)],
            mulc[*(src + 6)],
            mulc[*(src + 7)],

            mulc[*(src + 8)],
            mulc[*(src + 9)],
            mulc[*(src + 10)],
            mulc[*(src + 11)],

            mulc[*(src + 12)],
            mulc[*(src + 13)],
            mulc[*(src + 14)],
            mulc[*(src + 15)]
        );
        src += 16;
       
        xmm3 = _mm_setr_epi8
        (
            mulc[*(src + 0)],
            mulc[*(src + 1)],
            mulc[*(src + 2)],
            mulc[*(src + 3)],

            mulc[*(src + 4)],
            mulc[*(src + 5)],
            mulc[*(src + 6)],
            mulc[*(src + 7)],

            mulc[*(src + 8)],
            mulc[*(src + 9)],
            mulc[*(src + 10)],
            mulc[*(src + 11)],

            mulc[*(src + 12)],
            mulc[*(src + 13)],
            mulc[*(src + 14)],
            mulc[*(src + 15)]
        );
        src += 16;
       
        xmm4 = _mm_setr_epi8
        (
            mulc[*(src + 0)],
            mulc[*(src + 1)],
            mulc[*(src + 2)],
            mulc[*(src + 3)],

            mulc[*(src + 4)],
            mulc[*(src + 5)],
            mulc[*(src + 6)],
            mulc[*(src + 7)],

            mulc[*(src + 8)],
            mulc[*(src + 9)],
            mulc[*(src + 10)],
            mulc[*(src + 11)],

            mulc[*(src + 12)],
            mulc[*(src + 13)],
            mulc[*(src + 14)],
            mulc[*(src + 15)]
        );
        src += 16;
       
        xmm5 = _mm_setr_epi8
        (
            mulc[*(src + 0)],
            mulc[*(src + 1)],
            mulc[*(src + 2)],
            mulc[*(src + 3)],

            mulc[*(src + 4)],
            mulc[*(src + 5)],
            mulc[*(src + 6)],
            mulc[*(src + 7)],

            mulc[*(src + 8)],
            mulc[*(src + 9)],
            mulc[*(src + 10)],
            mulc[*(src + 11)],

            mulc[*(src + 12)],
            mulc[*(src + 13)],
            mulc[*(src + 14)],
            mulc[*(src + 15)]
        );
        src += 16;
       
        xmm6 = _mm_setr_epi8
        (
            mulc[*(src + 0)],
            mulc[*(src + 1)],
            mulc[*(src + 2)],
            mulc[*(src + 3)],

            mulc[*(src + 4)],
            mulc[*(src + 5)],
            mulc[*(src + 6)],
            mulc[*(src + 7)],

            mulc[*(src + 8)],
            mulc[*(src + 9)],
            mulc[*(src + 10)],
            mulc[*(src + 11)],

            mulc[*(src + 12)],
            mulc[*(src + 13)],
            mulc[*(src + 14)],
            mulc[*(src + 15)]
        );
        src += 16;
       
        xmm7 = _mm_setr_epi8
        (
            mulc[*(src + 0)],
            mulc[*(src + 1)],
            mulc[*(src + 2)],
            mulc[*(src + 3)],

            mulc[*(src + 4)],
            mulc[*(src + 5)],
            mulc[*(src + 6)],
            mulc[*(src + 7)],

            mulc[*(src + 8)],
            mulc[*(src + 9)],
            mulc[*(src + 10)],
            mulc[*(src + 11)],

            mulc[*(src + 12)],
            mulc[*(src + 13)],
            mulc[*(src + 14)],
            mulc[*(src + 15)]
        );
        src += 16;
       
        xmm8 = _mm_load_si128((__m128i*)(dst + (16 * 0)));
        xmm9 = _mm_load_si128((__m128i*)(dst + (16 * 1)));
        xmm10 = _mm_load_si128((__m128i*)(dst + (16 * 2)));
        xmm11 = _mm_load_si128((__m128i*)(dst + (16 * 3)));
        xmm12 = _mm_load_si128((__m128i*)(dst + (16 * 4)));
        xmm13 = _mm_load_si128((__m128i*)(dst + (16 * 5)));
        xmm14 = _mm_load_si128((__m128i*)(dst + (16 * 6)));
        xmm15 = _mm_load_si128((__m128i*)(dst + (16 * 7)));

        xmm0 = _mm_xor_si128(xmm0, xmm8);
        xmm1 = _mm_xor_si128(xmm1, xmm9);
        xmm2 = _mm_xor_si128(xmm2, xmm10);
        xmm3 = _mm_xor_si128(xmm3, xmm11);
        xmm4 = _mm_xor_si128(xmm4, xmm12);
        xmm5 = _mm_xor_si128(xmm5, xmm13);
        xmm6 = _mm_xor_si128(xmm6, xmm14);
        xmm7 = _mm_xor_si128(xmm7, xmm15);
    
        _mm_store_si128((__m128i*)(dst + (16 * 0)), xmm0);
        _mm_store_si128((__m128i*)(dst + (16 * 1)), xmm1);
        _mm_store_si128((__m128i*)(dst + (16 * 2)), xmm2);
        _mm_store_si128((__m128i*)(dst + (16 * 3)), xmm3);
        _mm_store_si128((__m128i*)(dst + (16 * 4)), xmm4);
        _mm_store_si128((__m128i*)(dst + (16 * 5)), xmm5);
        _mm_store_si128((__m128i*)(dst + (16 * 6)), xmm6);
        _mm_store_si128((__m128i*)(dst + (16 * 7)), xmm7);

        dst += 128;
        i += 128;
    }

    for( ; i < len; i++)
    {
        *dst++ ^= mulc[*src++];
    }
#elif defined (PORTABLE_32_BIT)
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

    for (int32_t count = ((len - i) / 64) - 1; count >= 0; count--)
    {
        xmm0 = _mm_setr_epi8
        (
            mulc[*(src + 0)],
            mulc[*(src + 1)],
            mulc[*(src + 2)],
            mulc[*(src + 3)],

            mulc[*(src + 4)],
            mulc[*(src + 5)],
            mulc[*(src + 6)],
            mulc[*(src + 7)],

            mulc[*(src + 8)],
            mulc[*(src + 9)],
            mulc[*(src + 10)],
            mulc[*(src + 11)],

            mulc[*(src + 12)],
            mulc[*(src + 13)],
            mulc[*(src + 14)],
            mulc[*(src + 15)]
        );
        src += 16;

        xmm1 = _mm_setr_epi8
        (
            mulc[*(src + 0)],
            mulc[*(src + 1)],
            mulc[*(src + 2)],
            mulc[*(src + 3)],

            mulc[*(src + 4)],
            mulc[*(src + 5)],
            mulc[*(src + 6)],
            mulc[*(src + 7)],

            mulc[*(src + 8)],
            mulc[*(src + 9)],
            mulc[*(src + 10)],
            mulc[*(src + 11)],

            mulc[*(src + 12)],
            mulc[*(src + 13)],
            mulc[*(src + 14)],
            mulc[*(src + 15)]
        );
        src += 16;
       
        xmm2 = _mm_setr_epi8
        (
            mulc[*(src + 0)],
            mulc[*(src + 1)],
            mulc[*(src + 2)],
            mulc[*(src + 3)],

            mulc[*(src + 4)],
            mulc[*(src + 5)],
            mulc[*(src + 6)],
            mulc[*(src + 7)],

            mulc[*(src + 8)],
            mulc[*(src + 9)],
            mulc[*(src + 10)],
            mulc[*(src + 11)],

            mulc[*(src + 12)],
            mulc[*(src + 13)],
            mulc[*(src + 14)],
            mulc[*(src + 15)]
        );
        src += 16;
       
        xmm3 = _mm_setr_epi8
        (
            mulc[*(src + 0)],
            mulc[*(src + 1)],
            mulc[*(src + 2)],
            mulc[*(src + 3)],

            mulc[*(src + 4)],
            mulc[*(src + 5)],
            mulc[*(src + 6)],
            mulc[*(src + 7)],

            mulc[*(src + 8)],
            mulc[*(src + 9)],
            mulc[*(src + 10)],
            mulc[*(src + 11)],

            mulc[*(src + 12)],
            mulc[*(src + 13)],
            mulc[*(src + 14)],
            mulc[*(src + 15)]
        );
        src += 16;
       
        xmm4 = _mm_load_si128((__m128i*)(dst + (16 * 0)));
        xmm5 = _mm_load_si128((__m128i*)(dst + (16 * 1)));
        xmm6 = _mm_load_si128((__m128i*)(dst + (16 * 2)));
        xmm7 = _mm_load_si128((__m128i*)(dst + (16 * 3)));

        xmm0 = _mm_xor_si128(xmm0, xmm4);
        xmm1 = _mm_xor_si128(xmm1, xmm5);
        xmm2 = _mm_xor_si128(xmm2, xmm6);
        xmm3 = _mm_xor_si128(xmm3, xmm7);
    
        _mm_store_si128((__m128i*)(dst + (16 * 0)), xmm0);
        _mm_store_si128((__m128i*)(dst + (16 * 1)), xmm1);
        _mm_store_si128((__m128i*)(dst + (16 * 2)), xmm2);
        _mm_store_si128((__m128i*)(dst + (16 * 3)), xmm3);

        dst += 64;
        i += 64;
    }

    for( ; i < len; i++)
    {
        *dst++ ^= mulc[*src++];
    }
#endif
}

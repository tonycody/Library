#include "stdafx.h"
#include "xor.h"

#include "xmmintrin.h" //SSE
#include "emmintrin.h" //SSE2
//#include "pmmintrin.h" //SSE3
//#include "tmmintrin.h" //SSSE3
//#include "smmintrin.h" //SSE4.1
//#include "nmmintrin.h" //SSE4.2
//#include "wmmintrin.h" //AES
//#include "immintrin.h" //AVX

void xor(byte* x, byte* y, byte* result, int32_t len)
{
#if defined (PORTABLE_64_BIT)
    if (len >= 16)
    {
        for (int32_t i = (len / 16) - 1; i >= 0; i--, x += 16, y += 16, result += 16)
        {
            __m128i xmm_x = _mm_loadu_si128((__m128i*)x);
            __m128i xmm_y = _mm_loadu_si128((__m128i*)y);
            __m128i xmm_res =  _mm_xor_si128(xmm_x, xmm_y);
            _mm_storeu_si128((__m128i*)result, xmm_res);
        }
    }

    if ((len & 8) != 0)
    {
        *((uint64_t*)result) = *((uint64_t*)x) ^ *((uint64_t*)y);
        x += 8; y += 8; result += 8;
    }

    if ((len & 4) != 0)
    {
        *((uint32_t*)result) = *((uint32_t*)x) ^ *((uint32_t*)y);
        x += 4; y += 4; result += 4;
    }

    if ((len & 2) != 0)
    {
        *((uint16_t*)result) = *((uint16_t*)x) ^ *((uint16_t*)y);
        x += 2; y += 2; result += 2;
    }

    if ((len & 1) != 0)
    {
        *((byte*)result) = (byte)(*((byte*)x) ^ *((byte*)y));
    }
#elif defined (PORTABLE_32_BIT)
    if (len >= 16)
    {
        for (int32_t i = (len / 16) - 1; i >= 0; i--, x += 16, y += 16, result += 16)
        {
            __m128i xmm_x = _mm_loadu_si128((__m128i*)x);
            __m128i xmm_y = _mm_loadu_si128((__m128i*)y);
            __m128i xmm_res =  _mm_xor_si128(xmm_x, xmm_y);
            _mm_storeu_si128((__m128i*)result, xmm_res);
        }
    }

    if ((len & 8) != 0)
    {
        *((uint32_t*)result) = *((uint32_t*)x) ^ *((uint32_t*)y);
        x += 4; y += 4; result += 4;
        *((uint32_t*)result) = *((uint32_t*)x) ^ *((uint32_t*)y);
        x += 4; y += 4; result += 4;
    }

    if ((len & 4) != 0)
    {
        *((uint32_t*)result) = *((uint32_t*)x) ^ *((uint32_t*)y);
        x += 4; y += 4; result += 4;
    }

    if ((len & 2) != 0)
    {
        *((uint16_t*)result) = *((uint16_t*)x) ^ *((uint16_t*)y);
        x += 2; y += 2; result += 2;
    }

    if ((len & 1) != 0)
    {
        *((byte*)result) = (byte)(*((byte*)x) ^ *((byte*)y));
    }
#endif
}

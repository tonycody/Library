#include "stdafx.h"
#include "Unsafe.h"

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

//#include <xmmintrin.h> // SSE
#include <smmintrin.h> // SSE2
//#include <pmmintrin.h> // SSE3
//#include <emmintrin.h> // SSE4

void copy(byte* src, byte* dst, int32_t len)
{
    if (len <= 32)
    {
        for(int32_t i = 0; i < len; i++)
        {
            *dst++ = *src++;
        }
    }
    else
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
            if(((uintptr_t)src % 16) == 0) break;

            *dst++ = *src++;
        }

        for (int32_t count = ((len - i) / 128) - 1; count >= 0 ; count--)
        {
            xmm0 = _mm_load_si128((__m128i*)src);
            xmm1 = _mm_load_si128((__m128i*)(src + 16));
            xmm2 = _mm_load_si128((__m128i*)(src + (16 * 2)));
            xmm3 = _mm_load_si128((__m128i*)(src + (16 * 3)));
            xmm4 = _mm_load_si128((__m128i*)(src + (16 * 4)));
            xmm5 = _mm_load_si128((__m128i*)(src + (16 * 5)));
            xmm6 = _mm_load_si128((__m128i*)(src + (16 * 6)));
            xmm7 = _mm_load_si128((__m128i*)(src + (16 * 7)));

            _mm_storeu_si128((__m128i*)dst, xmm0);
            _mm_storeu_si128((__m128i*)(dst + 16), xmm1);
            _mm_storeu_si128((__m128i*)(dst + (16 * 2)), xmm2);
            _mm_storeu_si128((__m128i*)(dst + (16 * 3)), xmm3);
            _mm_storeu_si128((__m128i*)(dst + (16 * 4)), xmm4);
            _mm_storeu_si128((__m128i*)(dst + (16 * 5)), xmm5);
            _mm_storeu_si128((__m128i*)(dst + (16 * 6)), xmm6);
            _mm_storeu_si128((__m128i*)(dst + (16 * 7)), xmm7);

            src += 128;
            dst += 128;
            i += 128;
        }

        for( ; i < len; i++)
        {
            *dst++ = *src++;
        }
    }
}

// https://gist.github.com/karthick18/1361842
bool equals(byte* x, byte* y, int32_t len)
{
#if defined (PORTABLE_64_BIT)
    for (int32_t i = (len / 16) - 1; i >= 0; i--, x += 16, y += 16)
    {
        __m128i xmm_x = _mm_loadu_si128((__m128i*)x);
        __m128i xmm_y = _mm_loadu_si128((__m128i*)y);
        __m128i xmm_cmp = _mm_cmpeq_epi16(xmm_x, xmm_y);
        if ((uint16_t)_mm_movemask_epi8(xmm_cmp) != (uint16_t)0xffff) return false; 
    }

    if ((len & 8) != 0)
    {
        if (*((uint64_t*)x) != *((uint64_t*)y)) return false;
        x += 8; y += 8;
    }

    if ((len & 4) != 0)
    {
        if (*((uint32_t*)x) != *((uint32_t*)y)) return false;
        x += 4; y += 4;
    }

    if ((len & 2) != 0)
    {
        if (*((uint16_t*)x) != *((uint16_t*)y)) return false;
        x += 2; y += 2;
    }

    if ((len & 1) != 0)
    {
        if (*((byte*)x) != *((byte*)y)) return false;
    }

    return true;
#elif defined (PORTABLE_32_BIT)
    for (int32_t i = (len / 16) - 1; i >= 0; i--, x += 16, y += 16)
    {
        __m128i xmm_x = _mm_loadu_si128((__m128i*)x);
        __m128i xmm_y = _mm_loadu_si128((__m128i*)y);
        __m128i xmm_cmp = _mm_cmpeq_epi16(xmm_x, xmm_y);
        if ((uint16_t)_mm_movemask_epi8(xmm_cmp) != (uint16_t)0xffff) return false; 
    }

    if ((len & 8) != 0)
    {
        if (*((uint32_t*)x) != *((uint32_t*)y)) return false;
        x += 4; y += 4;
        if (*((uint32_t*)x) != *((uint32_t*)y)) return false;
        x += 4; y += 4;
    }

    if ((len & 4) != 0)
    {
        if (*((uint32_t*)x) != *((uint32_t*)y)) return false;
        x += 4; y += 4;
    }

    if ((len & 2) != 0)
    {
        if (*((uint16_t*)x) != *((uint16_t*)y)) return false;
        x += 2; y += 2;
    }

    if ((len & 1) != 0)
    {
        if (*((byte*)x) != *((byte*)y)) return false;
    }

    return true;
#endif
}

int32_t compare(byte* x, byte* y, int32_t len)
{
    int32_t c = 0;

    for (; len > 0; len--)
    {
        c = (int32_t)*x++ - (int32_t)*y++;
        if(c != 0) return c;
    }

    return 0;
}

void xor(byte* x, byte* y, byte* result, int32_t len)
{
#if defined (PORTABLE_64_BIT)
    for (int32_t i = (len / 16) - 1; i >= 0; i--, x += 16, y += 16, result += 16)
    {
        __m128i xmm_x = _mm_loadu_si128((__m128i*)x);
        __m128i xmm_y = _mm_loadu_si128((__m128i*)y);
        __m128i xmm_res =  _mm_xor_si128(xmm_x, xmm_y);
        _mm_storeu_si128((__m128i*)result, xmm_res);
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
    for (int32_t i = (len / 16) - 1; i >= 0; i--, x += 16, y += 16, result += 16)
    {
        __m128i xmm_x = _mm_loadu_si128((__m128i*)x);
        __m128i xmm_y = _mm_loadu_si128((__m128i*)y);
        __m128i xmm_res =  _mm_xor_si128(xmm_x, xmm_y);
        _mm_storeu_si128((__m128i*)result, xmm_res);
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

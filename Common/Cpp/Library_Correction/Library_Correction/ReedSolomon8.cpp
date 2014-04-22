#include "stdafx.h"
#include "ReedSolomon8.h"

#if _WIN64 || __amd64__
#define PORTABLE_64_BIT
#else
#define PORTABLE_32_BIT
#endif

void mul(byte* src, byte* dst, byte* mulc, int32_t len)
{
#if defined (PORTABLE_64_BIT)
    uint64_t value1;
    uint64_t value2;
    uint64_t value3;
    uint64_t value4;
    uint64_t value5;
    uint64_t value6;
    uint64_t value7;
    uint64_t value8;

    byte* p1;
    byte* p2;
    byte* p3;
    byte* p4;
    byte* p5;
    byte* p6;
    byte* p7;
    byte* p8;

    register byte* end;
    end = dst + (len / 64) * 64;

    byte* end2;
    end2 = dst + len;

    while (dst != end)
    {
        p1 = (byte*)&value1;
        *p1++ = mulc[*src++];
        *p1++ = mulc[*src++];
        *p1++ = mulc[*src++];
        *p1++ = mulc[*src++];
        *p1++ = mulc[*src++];
        *p1++ = mulc[*src++];
        *p1++ = mulc[*src++];
        *p1 = mulc[*src++];
        p2 = (byte*)&value2;
        *p2++ = mulc[*src++];
        *p2++ = mulc[*src++];
        *p2++ = mulc[*src++];
        *p2++ = mulc[*src++];
        *p2++ = mulc[*src++];
        *p2++ = mulc[*src++];
        *p2++ = mulc[*src++];
        *p2 = mulc[*src++];
        p3 = (byte*)&value3;
        *p3++ = mulc[*src++];
        *p3++ = mulc[*src++];
        *p3++ = mulc[*src++];
        *p3++ = mulc[*src++];
        *p3++ = mulc[*src++];
        *p3++ = mulc[*src++];
        *p3++ = mulc[*src++];
        *p3 = mulc[*src++];
        p4 = (byte*)&value4;
        *p4++ = mulc[*src++];
        *p4++ = mulc[*src++];
        *p4++ = mulc[*src++];
        *p4++ = mulc[*src++];
        *p4++ = mulc[*src++];
        *p4++ = mulc[*src++];
        *p4++ = mulc[*src++];
        *p4 = mulc[*src++];
        p5 = (byte*)&value5;
        *p5++ = mulc[*src++];
        *p5++ = mulc[*src++];
        *p5++ = mulc[*src++];
        *p5++ = mulc[*src++];
        *p5++ = mulc[*src++];
        *p5++ = mulc[*src++];
        *p5++ = mulc[*src++];
        *p5 = mulc[*src++];
        p6 = (byte*)&value6;
        *p6++ = mulc[*src++];
        *p6++ = mulc[*src++];
        *p6++ = mulc[*src++];
        *p6++ = mulc[*src++];
        *p6++ = mulc[*src++];
        *p6++ = mulc[*src++];
        *p6++ = mulc[*src++];
        *p6 = mulc[*src++];
        p7 = (byte*)&value7;
        *p7++ = mulc[*src++];
        *p7++ = mulc[*src++];
        *p7++ = mulc[*src++];
        *p7++ = mulc[*src++];
        *p7++ = mulc[*src++];
        *p7++ = mulc[*src++];
        *p7++ = mulc[*src++];
        *p7 = mulc[*src++];
        p8 = (byte*)&value8;
        *p8++ = mulc[*src++];
        *p8++ = mulc[*src++];
        *p8++ = mulc[*src++];
        *p8++ = mulc[*src++];
        *p8++ = mulc[*src++];
        *p8++ = mulc[*src++];
        *p8++ = mulc[*src++];
        *p8 = mulc[*src++];

        *((uint64_t*)dst) ^= value1;
        dst += 8;
        *((uint64_t*)dst) ^= value2;
        dst += 8;
        *((uint64_t*)dst) ^= value3;
        dst += 8;
        *((uint64_t*)dst) ^= value4;
        dst += 8;
        *((uint64_t*)dst) ^= value5;
        dst += 8;
        *((uint64_t*)dst) ^= value6;
        dst += 8;
        *((uint64_t*)dst) ^= value7;
        dst += 8;
        *((uint64_t*)dst) ^= value8;
        dst += 8;
    }

    while (dst != end2)
    {
        *dst++ ^= mulc[*src++];
    }
#elif defined (PORTABLE_32_BIT)
    uint32_t value1;
    uint32_t value2;
    uint32_t value3;
    uint32_t value4;
    uint32_t value5;
    uint32_t value6;
    uint32_t value7;
    uint32_t value8;

    byte* p1;
    byte* p2;
    byte* p3;
    byte* p4;
    byte* p5;
    byte* p6;
    byte* p7;
    byte* p8;

    register byte* end;
    end = dst + (len / 32) * 32;

    byte* end2;
    end2 = dst + len;

    while (dst != end)
    {
        p1 = (byte*)&value1;
        *p1++ = mulc[*src++];
        *p1++ = mulc[*src++];
        *p1++ = mulc[*src++];
        *p1 = mulc[*src++];
        p2 = (byte*)&value2;
        *p2++ = mulc[*src++];
        *p2++ = mulc[*src++];
        *p2++ = mulc[*src++];
        *p2 = mulc[*src++];
        p3 = (byte*)&value3;
        *p3++ = mulc[*src++];
        *p3++ = mulc[*src++];
        *p3++ = mulc[*src++];
        *p3 = mulc[*src++];
        p4 = (byte*)&value4;
        *p4++ = mulc[*src++];
        *p4++ = mulc[*src++];
        *p4++ = mulc[*src++];
        *p4 = mulc[*src++];
        p5 = (byte*)&value5;
        *p5++ = mulc[*src++];
        *p5++ = mulc[*src++];
        *p5++ = mulc[*src++];
        *p5 = mulc[*src++];
        p6 = (byte*)&value6;
        *p6++ = mulc[*src++];
        *p6++ = mulc[*src++];
        *p6++ = mulc[*src++];
        *p6 = mulc[*src++];
        p7 = (byte*)&value7;
        *p7++ = mulc[*src++];
        *p7++ = mulc[*src++];
        *p7++ = mulc[*src++];
        *p7 = mulc[*src++];
        p8 = (byte*)&value8;
        *p8++ = mulc[*src++];
        *p8++ = mulc[*src++];
        *p8++ = mulc[*src++];
        *p8 = mulc[*src++];

        *((uint32_t*)dst) ^= value1;
        dst += 4;
        *((uint32_t*)dst) ^= value2;
        dst += 4;
        *((uint32_t*)dst) ^= value3;
        dst += 4;
        *((uint32_t*)dst) ^= value4;
        dst += 4;
        *((uint32_t*)dst) ^= value5;
        dst += 4;
        *((uint32_t*)dst) ^= value6;
        dst += 4;
        *((uint32_t*)dst) ^= value7;
        dst += 4;
        *((uint32_t*)dst) ^= value8;
        dst += 4;
    }

    while (dst != end2)
    {
        *dst++ ^= mulc[*src++];
    }
#endif
}

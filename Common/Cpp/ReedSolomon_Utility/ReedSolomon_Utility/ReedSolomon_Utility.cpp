#include "stdafx.h"
#include "ReedSolomon_Utility.h"

#if _WIN64 || __amd64__
#define PORTABLE_64_BIT
#else
#define PORTABLE_32_BIT
#endif

void mul(byte* src, byte* dst, byte* mulc, int32_t len)
{
#if defined (PORTABLE_64_BIT)
    register uint64_t value;

    for (; len >= 8; len -= 8)
    {
        byte* p = (byte*)&value;
        *p++ = mulc[*src++];
        *p++ = mulc[*src++];
        *p++ = mulc[*src++];
        *p++ = mulc[*src++];
        *p++ = mulc[*src++];
        *p++ = mulc[*src++];
        *p++ = mulc[*src++];
        *p = mulc[*src++];

        *((uint64_t*)dst) ^= value;
        dst += 8;
    }

    for (; len > 0; len--)
    {
        *dst++ ^= mulc[*src++];
    }
#elif defined (PORTABLE_32_BIT)
    register uint32_t value;

    for (; len >= 4; len -= 4)
    {
        byte* p = (byte*)&value;
        *p++ = mulc[*src++];
        *p++ = mulc[*src++];
        *p++ = mulc[*src++];
        *p = mulc[*src++];

        *((uint32_t*)dst) ^= value;
        dst += 4;
    }

    for (; len > 0; len--)
    {
        *dst++ ^= mulc[*src++];
    }
#endif
}

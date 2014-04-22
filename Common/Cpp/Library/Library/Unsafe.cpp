#include "stdafx.h"
#include "Unsafe.h"

#if _WIN64 || __amd64__
#define PORTABLE_64_BIT
#else
#define PORTABLE_32_BIT
#endif

bool equals(byte* x, byte* y, int32_t len)
{
#if defined (PORTABLE_64_BIT)
    for (int32_t i = (len / 8) - 1; i >= 0; i--, x += 8, y += 8)
    {
        if (*((uint64_t*)x) != *((uint64_t*)y)) return false;
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
    for (int32_t i = (len / 4) - 1; i >= 0; i--, x += 4, y += 4)
    {
        if (*((uint32_t*)x) != *((uint32_t*)y)) return false;
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
    for (int32_t i = (len / 8) - 1; i >= 0; i--, x += 8, y += 8, result += 8)
    {
        *((uint64_t*)result) = *((uint64_t*)x) ^ *((uint64_t*)y);
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
    for (int32_t i = (len / 4) - 1; i >= 0; i--, x += 4, y += 4, result += 4)
    {
        *((uint32_t*)result) = *((uint32_t*)x) ^ *((uint32_t*)y);
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

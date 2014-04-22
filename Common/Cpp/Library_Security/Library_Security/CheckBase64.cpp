#include "stdafx.h"
#include "CheckBase64.h"

bool checkBase64(uint16_t* source, int32_t length)
{
    for (int32_t i = length - 1; i >= 0 ; i--)
    {
        if (!((uint16_t)0x0041 <= *source && *source <= (uint16_t)0x005A)
            && !((uint16_t)0x0061 <= *source && *source <= (uint16_t)0x007A)
            && !((uint16_t)0x0030 <= *source && *source <= (uint16_t)0x0039)
            && !(*source == (uint16_t)0x002D || *source == (uint16_t)0x005F)) return false;

        source++;
    }

    return true;
}

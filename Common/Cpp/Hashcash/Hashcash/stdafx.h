// stdafx.h : include file for standard system include files,
// or project specific include files that are used frequently, but
// are changed infrequently
//

#pragma once

#include "targetver.h"

#include <stdio.h>
#include <stdint.h>
#include <tchar.h>
#include <windows.h>
#include <iostream>
#include <time.h>

//#include <xmmintrin.h> // SSE
#include <smmintrin.h> // SSE2
//#include <pmmintrin.h> // SSE3
//#include <emmintrin.h> // SSE4

#ifndef PORTABLE_32_BIT_TEST
    #if  _WIN64 || __amd64__
    #define PORTABLE_64_BIT
    #else
    #define PORTABLE_32_BIT
    #endif
#else
    #define PORTABLE_32_BIT
#endif

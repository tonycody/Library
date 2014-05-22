#pragma once

class Xorshift
{

// ----- C code -----
//uint32_t xXorshift() { 
//    static uint32_t x = 123456789;
//    static uint32_t y = 362436069;
//    static uint32_t z = 521288629;
//    static uint32_t w = 88675123; 
//  
//    uint32_t t = x ^ (x << 11);
//    x = y; y = z; z = w;
//    return w = (w ^ (w >> 19)) ^ (t ^ (t >> 8)); 
//}

private:
    uint32_t x;
    uint32_t y;
    uint32_t z;
    uint32_t w;

public:
    Xorshift();
    ~Xorshift();

    uint32_t Next();
};

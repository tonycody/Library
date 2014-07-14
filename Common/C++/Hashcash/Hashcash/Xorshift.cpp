#include "stdafx.h"
#include "Xorshift.h"

#include "osrng.h"

Xorshift::Xorshift()
{
    x = 123456789;
    y = 362436069;
    z = 521288629;
    w = 88675123; 

    CryptoPP::AutoSeededRandomPool rng;
    w ^= (uint32_t)rng.GenerateWord32();
}

Xorshift::~Xorshift()
{

}

uint32_t Xorshift::next()
{
    uint32_t t = x ^ (x << 11);
    x = y; y = z; z = w;

    return w = (w ^ (w >> 19)) ^ (t ^ (t >> 8)); 
}

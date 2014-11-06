#include "stdafx.h"
#include "hashcash1.h"

#include "Xorshift.h"

using std::cout;
using std::endl;
using std::string;
using std::exception;

#include "cryptlib.h"
using CryptoPP::Exception;

#include "sha.h"
using CryptoPP::SHA256;

byte* hashcash1_Create(byte* value, int32_t limit, int32_t timeout)
{
    try
    {
        clock_t clockStart, clockEnd;

        if (timeout != -1)
        {
            clockStart = clock();
        }

        SHA256 hash;
        Xorshift xorshift;

        const size_t hashSize = 32;

        byte currentState[hashSize * 2];
        byte currentResult[hashSize];

        byte finalState[hashSize * 2];
        byte finalResult[hashSize];

        memcpy(currentState + hashSize, value, hashSize);
        memcpy(finalState + hashSize, value, hashSize);

        // Initialize
        {
            ((uint32_t*)currentState)[0] = xorshift.next();
            ((uint32_t*)currentState)[1] = xorshift.next();
            ((uint32_t*)currentState)[2] = xorshift.next();
            ((uint32_t*)currentState)[3] = xorshift.next();
            ((uint32_t*)currentState)[4] = xorshift.next();
            ((uint32_t*)currentState)[5] = xorshift.next();
            ((uint32_t*)currentState)[6] = xorshift.next();
            ((uint32_t*)currentState)[7] = xorshift.next();

            hash.CalculateDigest(currentResult, currentState, hashSize * 2);
        }

        memcpy(finalState, currentState, hashSize * 2);
        memcpy(finalResult, currentResult, hashSize);

        // コイン数の算出
        if (limit != -1)
        {
            int32_t count = 0;

            for (int32_t i = 0; i < hashSize; i++)
            {
                for (int32_t j = 0; j < 8; j++)
                {
                    if(((finalResult[i] << j) & 0x80) == 0) count++;
                    else goto End1;
                }
            }
        End1:

            if (count >= limit) goto HIT;
        }

        for (;;)
        {
            ((uint32_t*)currentState)[0] = xorshift.next();
            ((uint32_t*)currentState)[1] = xorshift.next();
            ((uint32_t*)currentState)[2] = xorshift.next();
            ((uint32_t*)currentState)[3] = xorshift.next();
            ((uint32_t*)currentState)[4] = xorshift.next();
            ((uint32_t*)currentState)[5] = xorshift.next();
            ((uint32_t*)currentState)[6] = xorshift.next();
            ((uint32_t*)currentState)[7] = xorshift.next();

            hash.CalculateDigest(currentResult, currentState, hashSize * 2);

            for (int32_t i = 0; i < hashSize; i++)
            {
                int32_t c = finalResult[i] - currentResult[i];

                if (c < 0)
                {
                    break;
                }
                else if (c == 0)
                {
                    continue;
                }
                else
                {
                    memcpy(finalState, currentState, hashSize * 2);
                    memcpy(finalResult, currentResult, hashSize);

                    // コイン数の算出
                    if (limit != -1)
                    {
                        int32_t count = 0;

                        for (int32_t i = 0; i < hashSize; i++)
                        {
                            for (int32_t j = 0; j < 8; j++)
                            {
                                if(((finalResult[i] << j) & 0x80) == 0) count++;
                                else goto End2;
                            }
                        }
                    End2:

                        if (count >= limit) goto HIT;
                    }

                    break;
                }
            }

            if (timeout != -1)
            {
                clockEnd = clock();
            
                if (((clockEnd - clockStart) / CLOCKS_PER_SEC) > timeout)
                {
                    break;
                }
            }
        }

    HIT:

        byte* key = (byte*)malloc(hashSize);
        memcpy(key, finalState, hashSize);

        return key;
    }
    catch (exception& e)
    {
        throw e;
    }
}

int32_t hashcash1_Verify(byte* key, byte* value)
{
    SHA256 hash;
    Xorshift xorshift;

    const size_t hashSize = 32;

    byte currentState[hashSize * 2];
    byte currentResult[hashSize];

    memcpy(currentState, key, hashSize);
    memcpy(currentState + hashSize, value, hashSize);

    hash.CalculateDigest(currentResult, currentState, hashSize * 2);

    int32_t count = 0;

    for (int32_t i = 0; i < hashSize; i++)
    {
        for (int32_t j = 0; j < 8; j++)
        {
            if(((currentResult[i] << j) & 0x80) == 0) count++;
            else goto End;
        }
    }
End:

    return count;
}

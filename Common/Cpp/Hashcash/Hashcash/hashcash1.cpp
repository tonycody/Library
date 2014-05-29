#include "stdafx.h"
#include "hashcash1.h"

#include "xor.h"
#include "Xorshift.h"

using std::cout;
using std::endl;
using std::string;
using std::exception;

#include "cryptlib.h"
using CryptoPP::Exception;

#include "sha.h"
using CryptoPP::SHA512;

byte* hashcash1_Create(byte* value, size_t valueSize, int32_t timeout)
{
    try
    {
        clock_t clockStart, clockEnd;
        clockStart = clock();

        const size_t bufferSize = 1024 * 256;
        byte* buffer = (byte*)calloc(bufferSize, sizeof(byte));

        SHA512 hash;
        Xorshift xorshift;

        const size_t hashSize = 64;

        byte currentKey[hashSize];
        byte currentResult[hashSize];

        byte* finalKey = (byte*)malloc(hashSize);
        byte finalResult[hashSize];

        byte hashTemp[hashSize];
        byte xorTemp[hashSize];

        const size_t blockSize = (hashSize * 2) + valueSize;
        byte* block = (byte*)malloc(blockSize);

        // Initialize
        {
            memset(xorTemp, 0, hashSize);

            ((uint32_t*)currentKey)[0] = xorshift.Next();
            ((uint32_t*)currentKey)[1] = xorshift.Next();
            ((uint32_t*)currentKey)[2] = xorshift.Next();
            ((uint32_t*)currentKey)[3] = xorshift.Next();
            ((uint32_t*)currentKey)[4] = xorshift.Next();
            ((uint32_t*)currentKey)[5] = xorshift.Next();
            ((uint32_t*)currentKey)[6] = xorshift.Next();
            ((uint32_t*)currentKey)[7] = xorshift.Next();
            ((uint32_t*)currentKey)[8] = xorshift.Next();
            ((uint32_t*)currentKey)[9] = xorshift.Next();
            ((uint32_t*)currentKey)[10] = xorshift.Next();
            ((uint32_t*)currentKey)[11] = xorshift.Next();
            ((uint32_t*)currentKey)[12] = xorshift.Next();
            ((uint32_t*)currentKey)[13] = xorshift.Next();
            ((uint32_t*)currentKey)[14] = xorshift.Next();
            ((uint32_t*)currentKey)[15] = xorshift.Next();

            memcpy(block, currentKey, hashSize);
            memcpy(block + hashSize, value, valueSize);

            for (int32_t i = (bufferSize / hashSize) - 1; i >= 0 ; i--)
            {
                memcpy(block + hashSize + valueSize, xorTemp, hashSize);
                hash.CalculateDigest(hashTemp, block, blockSize);
                xor(hashTemp, xorTemp, xorTemp, hashSize);
        
                memcpy(buffer + (i * hashSize), xorTemp, hashSize);
            }

            hash.CalculateDigest(currentResult, buffer, bufferSize);

            memcpy(finalKey, currentKey, hashSize);
            memcpy(finalResult, currentResult, hashSize);
        }

        for (;;)
        {
            memset(xorTemp, 0, hashSize);

            ((uint32_t*)currentKey)[0] = xorshift.Next();
            ((uint32_t*)currentKey)[1] = xorshift.Next();
            ((uint32_t*)currentKey)[2] = xorshift.Next();
            ((uint32_t*)currentKey)[3] = xorshift.Next();
            ((uint32_t*)currentKey)[4] = xorshift.Next();
            ((uint32_t*)currentKey)[5] = xorshift.Next();
            ((uint32_t*)currentKey)[6] = xorshift.Next();
            ((uint32_t*)currentKey)[7] = xorshift.Next();
            ((uint32_t*)currentKey)[8] = xorshift.Next();
            ((uint32_t*)currentKey)[9] = xorshift.Next();
            ((uint32_t*)currentKey)[10] = xorshift.Next();
            ((uint32_t*)currentKey)[11] = xorshift.Next();
            ((uint32_t*)currentKey)[12] = xorshift.Next();
            ((uint32_t*)currentKey)[13] = xorshift.Next();
            ((uint32_t*)currentKey)[14] = xorshift.Next();
            ((uint32_t*)currentKey)[15] = xorshift.Next();

            memcpy(block, currentKey, hashSize);
            memcpy(block + hashSize, value, valueSize);

            for (int32_t i = (bufferSize / hashSize) - 1; i >= 0 ; i--)
            {
                memcpy(block + hashSize + valueSize, xorTemp, hashSize);
                hash.CalculateDigest(hashTemp, block, blockSize);
                xor(hashTemp, xorTemp, xorTemp, hashSize);
        
                memcpy(buffer + (i * hashSize), xorTemp, hashSize);
            }

            hash.CalculateDigest(currentResult, buffer, bufferSize);

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
                    memcpy(finalKey, currentKey, hashSize);
                    memcpy(finalResult, currentResult, hashSize);

                    break;
                }
            }

            clockEnd = clock();
            
            if (((clockEnd - clockStart) / CLOCKS_PER_SEC) > timeout)
            {
                break;
            }
        }

        free(buffer);
        free(block);

        return finalKey;
    }
    catch (exception& e)
    {
        throw e;
    }
}

int32_t hashcash1_Verify(byte* key, byte* value, size_t valueSize)
{
    try
    {
        const size_t bufferSize = 1024 * 256;
        byte* buffer = (byte*)calloc(bufferSize, sizeof(byte));

        SHA512 hash;

        const size_t hashSize = 64;

        byte result[hashSize];

        byte hashTemp[hashSize];
        byte xorTemp[hashSize];

        const size_t blockSize = (hashSize * 2) + valueSize;
        byte* block = (byte*)malloc(blockSize);

        // Initialize
        {
            memset(xorTemp, 0, hashSize);

            memcpy(block, key, hashSize);
            memcpy(block + hashSize, value, valueSize);

            for (int32_t i = (bufferSize / hashSize) - 1; i >= 0 ; i--)
            {
                memcpy(block + hashSize + valueSize, xorTemp, hashSize);
                hash.CalculateDigest(hashTemp, block, blockSize);
                xor(hashTemp, xorTemp, xorTemp, hashSize);
        
                memcpy(buffer + (i * hashSize), xorTemp, hashSize);
            }

            hash.CalculateDigest(result, buffer, bufferSize);
        }

        free(buffer);
        free(block);

        int32_t count = 0;

        for (int32_t i = 0; i < hashSize; i++)
        {
            for (int32_t j = 0; j < 8; j++)
            {
                if(((result[i] << j) & 0x80) == 0) count++;
                else goto End;
            }
        }
End:

        return count;
    }
    catch (exception& e)
    {
        throw e;
    }
}

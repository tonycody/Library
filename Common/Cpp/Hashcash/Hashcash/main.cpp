#include "stdafx.h"
#include "hashcash1.h"

using std::cout;
using std::endl;
using std::string;
using std::exception;

inline char getHexValue(int32_t c)
{
    if (c < 10) return (char)(c + '0');
    else return (char)(c - 10 + 'a');
}

string toHexString(byte* value, size_t length)
{
    char* chars = (char*)malloc(((length * 2) + 1) * sizeof(char));

    {
        byte* t_value = value;
        char* t_chars = chars;

        for (int32_t i = length - 1; i >= 0; i--)
        {
            byte b = *t_value++;

            *t_chars++ = getHexValue(b >> 4);
            *t_chars++ = getHexValue(b & 0x0F);
        }
    
        *t_chars = '\0';
    }

    string result = chars;

    free(chars);

    return result;
}

byte* fromHexString(string value, size_t& size)
{
    if (value.length() % 2 != 0)
    {
        value = "0" + value;
    }

    size = (value.length() / 2) * sizeof(byte);
    byte* buffer = (byte*)malloc(size);

    {
        byte* t_buffer = buffer;
        char* t_value = (char*)value.c_str();

        for (int32_t i = size - 1; i >= 0; i--)
        {
            int32_t i1 = 0, i2 = 0;

            if ('0' <= *t_value && *t_value <= '9')
            {
                i1 = *t_value - '0';
            }
            else if ('a' <= *t_value && *t_value <= 'f')
            {
                i1 = (*t_value - 'a') + 10;
            }
            else if ('A' <= *t_value && *t_value <= 'F')
            {
                i1 = (*t_value - 'A') + 10;
            }

            t_value++;

            if ('0' <= *t_value && *t_value <= '9')
            {
                i2 = *t_value - '0';
            }
            else if ('a' <= *t_value && *t_value <= 'f')
            {
                i2 = (*t_value - 'a') + 10;
            }
            else if ('A' <= *t_value && *t_value <= 'F')
            {
                i2 = (*t_value - 'A') + 10;
            }

            t_value++;

            *t_buffer++ = (byte)((i1 << 4) | i2);
        }
    }

    return buffer;
}

//#define TEST

#ifdef TEST
int main2(int argc, char* argv[])
#else
int main(int argc, char* argv[])
#endif
{
    try
    {
        //{
        //    string s = "010203041245789865124578";
        //    size_t valueSize;
        //    byte* value = fromHexString(s, valueSize);
        //
        //    string key = toHexString(value, valueSize);
        //}

        if ((string)argv[1] == "hashcash1")
        {
            if((string)argv[2] == "create")
            {
                size_t valueSize;
                byte* value = fromHexString((string)argv[3], valueSize);
                
                int32_t timeout = atoi(argv[4]);

                byte* key = hashcash1_Create(value, valueSize, timeout);

                cout << toHexString(key, 64) << endl;

                free(key);

                free(value);
            }
            else if((string)argv[2] == "verify")
            {
                size_t keySize;
                byte* key = fromHexString((string)argv[3], keySize);
                if (keySize != 64) return 1;

                size_t valueSize;
                byte* value = fromHexString((string)argv[4], valueSize);

                int32_t count = hashcash1_Verify(key, value, valueSize);

                free(value);

                cout << count << endl;

                free(key);
            }
        }
    }
    catch (exception&)
    {
        return 1;
    }

    return 0;
}

#ifdef TEST
int main(int argc, char* argv[])
{
    clock_t clockStart, clockEnd;
    clockStart = clock();

    //{
    //    const int32_t count = 5;

    //    char* arguments[count];
    //    arguments[1] = "hashcash1";
    //    arguments[2] = "create";
    //    arguments[3] = "0101010101010101";
    //    arguments[4] = "600";

    //    main2(count, arguments);
    //}

    {
        const uint32_t count = 5;

        char* arguments[count];
        arguments[1] = "hashcash1";
        arguments[2] = "verify";
        
        // 5seconds, 4bit
        //arguments[3] = "9b243b8ab151fe55e04467904c3a2424afdd68772a79bcd09967567ac5c4a57f3beb114efabe577d6674f4b4d10d26e57d6aaf247ca0cfe4c7af8ff3e27c1226";
        // 60seconds, 11bit
        //arguments[3] = "8f0500bfcb091b83722ab7e2cd0b1d9fdc498e205749807bef183f20463c0b57d25f9905c7642a7ccdf30aa40602edaa46bf456575e3664a6fe501b97b3d3e7b";
        // 600seconds, 13bit
        arguments[3] = "eaf65296957c3b820b0d6c83d336a932ca734d33e27ecb6ae5222c89bd8248f21d6cc3aac92bce9b5f091d73eb30f6c5cc0fe474faf29e9e87475205ae5e5771";

        arguments[4] = "0101010101010101";

        main2(count, arguments);
    }

    clockEnd = clock();
         
    cout << (clockEnd - clockStart) << endl;

    return 0;
}
#endif

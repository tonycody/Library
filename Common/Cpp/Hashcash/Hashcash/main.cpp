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
    //    arguments[4] = "3600";

    //    main2(count, arguments);
    //}

    {
        const uint32_t count = 5;

        char* arguments[count];
        arguments[1] = "hashcash1";
        arguments[2] = "verify";
        // 60seconds, 9bit
        //arguments[3] = "9fcc47a072aec40958c2adc4f8d557b979d5ea27c858710bfce464a13d1d9ba68a55abd7afe09d5684ad58d0473054ae0227de234e23ff9a1889de8fac460780";
        // 600seconds, 12bit
        //arguments[3] = "23788335b0ec58deae9b689e713d9bb6e109f798234bd3815c05bc5befcba3349b743b146cafaa0f20dd87b4ec84519e11a4015192b08a0b1e026e813b800a93";
        // 1800seconds, 17bit
        //arguments[3] = "e4e66c96f10ca5b904273d6ebf4695052fdb3ff4dd836c65738deee22f8fe14d1fc30d4748d27e46e14a6dd0343fb491260388b8ed6ad408541354b2b5d72982";
        // 3600seconds, 20bit
        arguments[3] = "fdb5f592cc2a0617943035c4b7c634a7f1d551987ddc27bebdeafbd30b8ffbd2f83613c433de5744067af548c4277846057d811b009edde177ed1b02f7acda85";

        arguments[4] = "0101010101010101";

        main2(count, arguments);
    }

    clockEnd = clock();
         
    cout << (clockEnd - clockStart) << endl;

    return 0;
}
#endif

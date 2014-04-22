#pragma once

class Crc32_Castagnoli
{
public:
    Crc32_Castagnoli();
    ~Crc32_Castagnoli();

    uint32_t compute(uint32_t x, byte* source, int32_t length);

private:
    volatile uint32_t _table[256];
} _crc32_castagnoli;

uint32_t compute_Crc32_Castagnoli(uint32_t x, byte* source, int32_t length);

#pragma once

byte* hashcash1_Create(byte* value, size_t valueSize, int32_t timeout);
int32_t hashcash1_Verify(byte* key, byte* value, size_t valueSize);

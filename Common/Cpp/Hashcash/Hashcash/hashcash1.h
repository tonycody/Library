#pragma once

byte* hashcash1_Create(byte* value, int32_t timeout);
int32_t hashcash1_Verify(byte* key, byte* value);

/*
* fec.c -- forward error correction based on Vandermonde matrices
* 980614
* (C) 1997-98 Luigi Rizzo (luigi@iet.unipi.it)
*
* Portions derived from code by Phil Karn (karn@ka9q.ampr.org),
* Robert Morelos-Zaragoza (robert@spectra.eng.hawaii.edu) and Hari
* Thirumoorthy (harit@spectra.eng.hawaii.edu), Aug 1995
*
* Redistribution and use in source and binary forms, with or without
* modification, are permitted provided that the following conditions
* are met:
*
* 1. Redistributions of source code must retain the above copyright
*    notice, this list of conditions and the following disclaimer.
* 2. Redistributions in binary form must reproduce the above
*    copyright notice, this list of conditions and the following
*    disclaimer in the documentation and/or other materials
*    provided with the distribution.
*
* THIS SOFTWARE IS PROVIDED BY THE AUTHORS ``AS IS'' AND
* ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO,
* THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
* PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE AUTHORS
* BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY,
* OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
* PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA,
* OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
* THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR
* TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT
* OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY
* OF SUCH DAMAGE.
*/

/*
* The following parameter defines how many bits are used for
* field elements. The code supports any value from 2 to 16
* but fastest operation is achieved with 8 bit elements
* This is the only parameter you may want to change.
*/

#pragma once

#ifdef __cplusplus
extern "C" {
#endif

#define GF_BITS 8

#ifndef GF_BITS
#error GF_BITS NOT DEFINED!
#endif

#if defined(__GNUC__) || !defined(_WIN32)
#include <stdint.h>
#else
#ifndef uint8_t
#define uint8_t unsigned char
#endif
#ifndef uint16_t
#define uint16_t unsigned short
#endif
#ifndef uint32_t
#define uint32_t unsigned int
#endif
#endif

#if (GF_BITS <= 8)
typedef uint8_t gf;
#else
typedef uint16_t gf;
#endif

volatile int _cancel;

struct fec_parms {
    unsigned long magic ;
    int k, n ;		/* parameters of the code */
    gf *enc_matrix ;
} ;

#define	GF_SIZE ((1 << GF_BITS) - 1)	/* powers of \alpha */
void fec_free(struct fec_parms *p);
struct fec_parms * fec_new(int k, int n);
void init_fec();
void fec_encode(struct fec_parms *code, gf *src[], gf *fec, int index, int sz);
int fec_decode(struct fec_parms *code, gf *pkt[], int index[], int sz);
void set(int cancel);
int get();

/* end of file */

#ifdef __cplusplus
}
#endif
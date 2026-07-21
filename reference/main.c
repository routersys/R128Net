#ifndef _USE_MATH_DEFINES
#define _USE_MATH_DEFINES
#endif

#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "ebur128.c"

#include "dump.h"

char g_outdir[1024];

static const unsigned long kSampleRates[] = {
  16, 100, 8000, 11025, 16000, 22050, 24000, 32000, 44100, 44101,
  47999, 48000, 64000, 88200, 96000, 176400, 192000, 352800, 384000,
  2822400
};

static const int kSampleRateCount =
    (int)(sizeof(kSampleRates) / sizeof(kSampleRates[0]));

static void DumpMeta(void) {
  int major, minor, patch;
  double meta[8];

  ebur128_get_version(&major, &minor, &patch);

  meta[0] = (double)major;
  meta[1] = (double)minor;
  meta[2] = (double)patch;
  meta[3] = (double)sizeof(unsigned long);
  meta[4] = (double)sizeof(size_t);
  meta[5] = relative_gate;
  meta[6] = (double)FILTER_STATE_SIZE;
  meta[7] = ALMOST_ZERO;

  Write1D("meta", meta, 8);
}

static void DumpFilterCoefficients(void) {
  double *rates = (double *)malloc(sizeof(double) * (size_t)kSampleRateCount);
  double *coeffs =
      (double *)malloc(sizeof(double) * (size_t)kSampleRateCount * 10);
  double *geometry =
      (double *)malloc(sizeof(double) * (size_t)kSampleRateCount * 4);

  for (int r = 0; r < kSampleRateCount; ++r) {
    unsigned long rate = kSampleRates[r];
    ebur128_state *m = ebur128_init(1, rate, EBUR128_MODE_M);
    ebur128_state *s = ebur128_init(1, rate, EBUR128_MODE_S);

    rates[r] = (double)rate;

    if (m == NULL) {
      printf("ebur128_init failed for rate %lu in mode M\n", rate);
      exit(1);
    }

    for (int i = 0; i < 5; ++i) {
      coeffs[(size_t)r * 10 + (size_t)i] = m->d->b[i];
      coeffs[(size_t)r * 10 + 5 + (size_t)i] = m->d->a[i];
    }

    geometry[(size_t)r * 4] = (double)m->d->samples_in_100ms;
    geometry[(size_t)r * 4 + 1] = (double)m->d->audio_data_frames;
    geometry[(size_t)r * 4 + 2] = s != NULL ? (double)s->d->audio_data_frames : 0.0;
    geometry[(size_t)r * 4 + 3] = s != NULL ? 1.0 : 0.0;

    ebur128_destroy(&m);
    if (s != NULL) {
      ebur128_destroy(&s);
    }
  }

  Write1D("filter_rates", rates, kSampleRateCount);
  Write2D("filter_coeffs", coeffs, kSampleRateCount, 10);
  Write2D("filter_geometry", geometry, kSampleRateCount, 4);

  free(geometry);
  free(coeffs);
  free(rates);
}

int main(int argc, char *argv[]) {
  if (argc < 2) {
    printf("usage: r128ref <output directory>\n");
    return 1;
  }

  snprintf(g_outdir, sizeof(g_outdir), "%s", argv[1]);

  DumpMeta();
  DumpFilterCoefficients();

  printf("reference data written to %s\n", g_outdir);
  return 0;
}

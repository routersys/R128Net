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

static uint32_t g_seed;

static void ResetSeed(void) {
  g_seed = 2463534242u;
}

static double NextUnit(void) {
  g_seed ^= g_seed << 13;
  g_seed ^= g_seed >> 17;
  g_seed ^= g_seed << 5;
  return (double)g_seed / 4294967296.0;
}

static void DumpTranscendentals(void) {
  const int kProbeCount = 20000;
  double *input = (double *)malloc(sizeof(double) * (size_t)kProbeCount);
  double *output = (double *)malloc(sizeof(double) * (size_t)kProbeCount);

  ResetSeed();
  for (int i = 0; i < kProbeCount; ++i) {
    input[i] = NextUnit() * (M_PI / 2.0);
  }
  for (int i = 0; i < kProbeCount; ++i) output[i] = tan(input[i]);
  Write1D("tr_tan_input", input, kProbeCount);
  Write1D("tr_tan_output", output, kProbeCount);

  ResetSeed();
  for (int i = 0; i < kProbeCount; ++i) {
    input[i] = pow(10.0, NextUnit() * 24.0 - 16.0);
  }
  for (int i = 0; i < kProbeCount; ++i) output[i] = log(input[i]);
  Write1D("tr_log_input", input, kProbeCount);
  Write1D("tr_log_output", output, kProbeCount);

  ResetSeed();
  for (int i = 0; i < kProbeCount; ++i) {
    input[i] = NextUnit() * 24.0 - 16.0;
  }
  for (int i = 0; i < kProbeCount; ++i) output[i] = pow(10.0, input[i]);
  Write1D("tr_pow10_input", input, kProbeCount);
  Write1D("tr_pow10_output", output, kProbeCount);

  free(output);
  free(input);
}

static void DumpHistogramTables(void) {
  ebur128_state *st =
      ebur128_init(1, 48000, EBUR128_MODE_I | EBUR128_MODE_HISTOGRAM);
  if (st == NULL) {
    printf("ebur128_init failed for the histogram tables\n");
    exit(1);
  }

  Write1D("histogram_energies", histogram_energies, 1000);
  Write1D("histogram_boundaries", histogram_energy_boundaries, 1001);
  WriteScalar("relative_gate_factor", relative_gate_factor);
  WriteScalar("minus_twenty_decibels", minus_twenty_decibels);

  ebur128_destroy(&st);
}

static void DumpFilterOutput(void) {
  const unsigned int channels = 2;
  const unsigned long samplerate = 48000;
  const size_t frames = 19200;
  const size_t samples = frames * channels;

  double *input = (double *)malloc(sizeof(double) * samples);
  ResetSeed();
  for (size_t i = 0; i < samples; ++i) {
    input[i] = NextUnit() * 2.0 - 1.0;
  }

  ebur128_state *st = ebur128_init(channels, samplerate, EBUR128_MODE_M);
  if (st == NULL) {
    printf("ebur128_init failed for the filter output dump\n");
    exit(1);
  }

  if (st->d->audio_data_frames != frames) {
    printf("unexpected audio_data_frames %zu\n", st->d->audio_data_frames);
    exit(1);
  }

  ebur128_add_frames_double(st, input, frames);

  Write2D("filter_input", input, (int)frames, (int)channels);
  Write2D("filter_output", st->d->audio_data, (int)frames, (int)channels);

  double state[10];
  for (unsigned int c = 0; c < channels; ++c) {
    for (int j = 0; j < FILTER_STATE_SIZE; ++j) {
      state[c * FILTER_STATE_SIZE + j] = st->d->v[c][j];
    }
  }
  Write2D("filter_state", state, (int)channels, FILTER_STATE_SIZE);
  WriteScalar("filter_output_index", (double)st->d->audio_data_index);

  ebur128_destroy(&st);
  free(input);
}

static void DumpDenormalDecay(void) {
  const unsigned int channels = 1;
  const unsigned long samplerate = 48000;
  const size_t chunk = 4800;
  const int chunks = 40;

  double *noise = (double *)malloc(sizeof(double) * chunk);
  double *silence = (double *)calloc(chunk, sizeof(double));

  ResetSeed();
  for (size_t i = 0; i < chunk; ++i) {
    noise[i] = NextUnit() * 2.0 - 1.0;
  }

  ebur128_state *st = ebur128_init(channels, samplerate, EBUR128_MODE_M);
  if (st == NULL) {
    printf("ebur128_init failed for the denormal decay dump\n");
    exit(1);
  }

  ebur128_add_frames_double(st, noise, chunk);

  double *trace = (double *)malloc(
      sizeof(double) * (size_t)chunks * FILTER_STATE_SIZE);
  for (int k = 0; k < chunks; ++k) {
    ebur128_add_frames_double(st, silence, chunk);
    for (int j = 0; j < FILTER_STATE_SIZE; ++j) {
      trace[(size_t)k * FILTER_STATE_SIZE + (size_t)j] = st->d->v[0][j];
    }
  }

  Write2D("denormal_decay", trace, chunks, FILTER_STATE_SIZE);

  free(trace);
  ebur128_destroy(&st);
  free(silence);
  free(noise);
}

static void DumpDenormalGrowth(void) {
  const unsigned int channels = 1;
  const unsigned long samplerate = 48000;
  const size_t chunk = 4800;
  const int chunks = 9000;
  const int stride = 100;
  const int rows = chunks / stride;

  double *noise = (double *)malloc(sizeof(double) * chunk);
  double *silence = (double *)calloc(chunk, sizeof(double));

  ResetSeed();
  for (size_t i = 0; i < chunk; ++i) {
    noise[i] = NextUnit() * 2.0 - 1.0;
  }

  ebur128_state *st = ebur128_init(channels, samplerate, EBUR128_MODE_M);
  if (st == NULL) {
    printf("ebur128_init failed for the denormal growth dump\n");
    exit(1);
  }

  ebur128_add_frames_double(st, noise, chunk);

  double *trace = (double *)malloc(sizeof(double) * (size_t)rows * 3);
  for (int k = 0; k < chunks; ++k) {
    ebur128_add_frames_double(st, silence, chunk);
    if ((k + 1) % stride == 0) {
      int row = ((k + 1) / stride) - 1;
      double peak = 0.0;
      for (int j = 0; j < FILTER_STATE_SIZE; ++j) {
        double magnitude = fabs(st->d->v[0][j]);
        if (magnitude > peak) {
          peak = magnitude;
        }
      }
      double momentary;
      ebur128_loudness_momentary(st, &momentary);
      trace[(size_t)row * 3] = (double)((k + 1) * chunk);
      trace[(size_t)row * 3 + 1] = peak;
      trace[(size_t)row * 3 + 2] = momentary;
    }
  }

  Write2D("denormal_growth", trace, rows, 3);

  free(trace);
  ebur128_destroy(&st);
  free(silence);
  free(noise);
}

int main(int argc, char *argv[]) {
  if (argc < 2) {
    printf("usage: r128ref <output directory>\n");
    return 1;
  }

  snprintf(g_outdir, sizeof(g_outdir), "%s", argv[1]);

  DumpMeta();
  DumpFilterCoefficients();
  DumpTranscendentals();
  DumpHistogramTables();
  DumpFilterOutput();
  DumpDenormalDecay();
  DumpDenormalGrowth();

  printf("reference data written to %s\n", g_outdir);
  return 0;
}

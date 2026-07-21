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

static void DumpInterpolator(void) {
  static const unsigned long rates[2] = { 48000, 96000 };
  char name[256];

  for (int r = 0; r < 2; ++r) {
    ebur128_state *st = ebur128_init(1, rates[r], EBUR128_MODE_TRUE_PEAK);
    if (st == NULL || st->d->interp == NULL) {
      printf("ebur128_init failed for the interpolator dump\n");
      exit(1);
    }

    interpolator *interp = st->d->interp;
    unsigned int factor = interp->factor;

    double header[4];
    header[0] = (double)interp->factor;
    header[1] = (double)interp->taps;
    header[2] = (double)interp->delay;
    header[3] = (double)interp->channels;
    snprintf(name, sizeof(name), "interp_header_%lu", rates[r]);
    Write1D(name, header, 4);

    double *counts = (double *)malloc(sizeof(double) * factor);
    for (unsigned int f = 0; f < factor; ++f) {
      counts[f] = (double)interp->filter[f].count;
    }
    snprintf(name, sizeof(name), "interp_counts_%lu", rates[r]);
    Write1D(name, counts, (int)factor);
    free(counts);

    double *coeff = (double *)malloc(
        sizeof(double) * (size_t)factor * interp->delay);
    double *index = (double *)malloc(
        sizeof(double) * (size_t)factor * interp->delay);
    for (unsigned int f = 0; f < factor; ++f) {
      for (unsigned int t = 0; t < interp->delay; ++t) {
        coeff[(size_t)f * interp->delay + t] = interp->filter[f].coeff[t];
        index[(size_t)f * interp->delay + t] =
            (double)interp->filter[f].index[t];
      }
    }
    snprintf(name, sizeof(name), "interp_coeff_%lu", rates[r]);
    Write2D(name, coeff, (int)factor, (int)interp->delay);
    snprintf(name, sizeof(name), "interp_index_%lu", rates[r]);
    Write2D(name, index, (int)factor, (int)interp->delay);
    free(index);
    free(coeff);

    ebur128_destroy(&st);
  }
}

static const double kAmplitudes[10] = {
  1.0, 0.5, 0.25, 0.1, 0.03, 0.008, 0.0, 0.7, 0.002, 0.35
};

static double *BuildLoudnessInput(unsigned int channels, unsigned long fs,
    size_t frames) {
  double *input = (double *)malloc(sizeof(double) * frames * channels);
  ResetSeed();
  for (size_t i = 0; i < frames; ++i) {
    double amplitude = kAmplitudes[(i / (size_t)fs) % 10];
    for (unsigned int c = 0; c < channels; ++c) {
      input[i * channels + c] = (NextUnit() * 2.0 - 1.0) * amplitude;
    }
  }
  return input;
}

static void DumpBlockList(const char *name,
    struct ebur128_double_queue *list) {
  size_t count = 0;
  struct ebur128_dq_entry *it;
  STAILQ_FOREACH(it, list, entries) {
    ++count;
  }

  double *values = (double *)malloc(sizeof(double) * (count == 0 ? 1 : count));
  size_t j = 0;
  STAILQ_FOREACH(it, list, entries) {
    values[j++] = it->z;
  }

  Write1D(name, values, (int)count);
  free(values);
}

static void DumpLoudness(const char *suffix, unsigned int channels,
    unsigned long fs, int seconds, int mode, const int *channelMap,
    size_t chunkFrames) {
  size_t frames = (size_t)fs * (size_t)seconds;
  double *input = BuildLoudnessInput(channels, fs, frames);
  char name[256];

  ebur128_state *st = ebur128_init(channels, fs, mode);
  if (st == NULL) {
    printf("ebur128_init failed for the loudness dump %s\n", suffix);
    exit(1);
  }

  if (channelMap != NULL) {
    for (unsigned int c = 0; c < channels; ++c) {
      ebur128_set_channel(st, c, channelMap[c]);
    }
  }

  snprintf(name, sizeof(name), "loudness_head_%s", suffix);
  Write2D(name, input, 4800, (int)channels);

  for (size_t offset = 0; offset < frames; offset += chunkFrames) {
    size_t take = frames - offset;
    if (take > chunkFrames) {
      take = chunkFrames;
    }
    ebur128_add_frames_double(st, input + offset * channels, take);
  }

  double results[6];
  int errors[6];
  errors[0] = ebur128_loudness_global(st, &results[0]);
  errors[1] = ebur128_loudness_momentary(st, &results[1]);
  errors[2] = ebur128_loudness_shortterm(st, &results[2]);
  errors[3] = ebur128_loudness_range(st, &results[3]);
  errors[4] = ebur128_relative_threshold(st, &results[4]);
  errors[5] = ebur128_loudness_window(st, 400, &results[5]);

  for (int i = 0; i < 6; ++i) {
    if (errors[i] != EBUR128_SUCCESS) {
      results[i] = (double)(-1000000 - errors[i]);
    }
  }

  snprintf(name, sizeof(name), "loudness_results_%s", suffix);
  Write1D(name, results, 6);

  double *peaks = (double *)malloc(sizeof(double) * channels * 4);
  for (unsigned int c = 0; c < channels; ++c) {
    double value;
    peaks[c * 4] = ebur128_sample_peak(st, c, &value) == EBUR128_SUCCESS
        ? value : -1.0;
    peaks[c * 4 + 1] = ebur128_true_peak(st, c, &value) == EBUR128_SUCCESS
        ? value : -1.0;
    peaks[c * 4 + 2] = ebur128_prev_sample_peak(st, c, &value) == EBUR128_SUCCESS
        ? value : -1.0;
    peaks[c * 4 + 3] = ebur128_prev_true_peak(st, c, &value) == EBUR128_SUCCESS
        ? value : -1.0;
  }
  snprintf(name, sizeof(name), "loudness_peaks_%s", suffix);
  Write2D(name, peaks, (int)channels, 4);
  free(peaks);

  snprintf(name, sizeof(name), "loudness_blocks_%s", suffix);
  DumpBlockList(name, &st->d->block_list);
  snprintf(name, sizeof(name), "loudness_shortterm_%s", suffix);
  DumpBlockList(name, &st->d->short_term_block_list);

  if (st->d->use_histogram) {
    double *bins = (double *)malloc(sizeof(double) * 1000 * 2);
    for (int i = 0; i < 1000; ++i) {
      bins[i * 2] = (double)st->d->block_energy_histogram[i];
      bins[i * 2 + 1] = (double)st->d->short_term_block_energy_histogram[i];
    }
    snprintf(name, sizeof(name), "loudness_histogram_%s", suffix);
    Write2D(name, bins, 1000, 2);
    free(bins);
  }

  double geometry[4];
  geometry[0] = (double)st->d->audio_data_index;
  geometry[1] = (double)st->d->audio_data_frames;
  geometry[2] = (double)st->d->needed_frames;
  geometry[3] = (double)st->d->short_term_frame_counter;
  snprintf(name, sizeof(name), "loudness_geometry_%s", suffix);
  Write1D(name, geometry, 4);

  ebur128_destroy(&st);
  free(input);
}

static const double kExtremeAmplitudes[10] = {
  1.0, 1e-20, 1e-40, 1e-310, 0.0, 1.0, 1e-45, 5e-324, 0.5, 1e-300
};

static void DumpExtreme(void) {
  const unsigned int channels = 2;
  const unsigned long fs = 48000;
  const int seconds = 10;
  const size_t chunkFrames = 4801;
  size_t frames = (size_t)fs * (size_t)seconds;

  double *input = (double *)malloc(sizeof(double) * frames * channels);
  ResetSeed();
  for (size_t i = 0; i < frames; ++i) {
    double amplitude = kExtremeAmplitudes[(i / (size_t)fs) % 10];
    for (unsigned int c = 0; c < channels; ++c) {
      input[i * channels + c] = (NextUnit() * 2.0 - 1.0) * amplitude;
    }
  }

  ebur128_state *st = ebur128_init(channels, fs,
      EBUR128_MODE_I | EBUR128_MODE_LRA | EBUR128_MODE_TRUE_PEAK);
  if (st == NULL) {
    printf("ebur128_init failed for the extreme dump\n");
    exit(1);
  }

  Write2D("extreme_head", input, 4800, (int)channels);

  for (size_t offset = 0; offset < frames; offset += chunkFrames) {
    size_t take = frames - offset;
    if (take > chunkFrames) {
      take = chunkFrames;
    }
    ebur128_add_frames_double(st, input + offset * channels, take);
  }

  double results[6];
  ebur128_loudness_global(st, &results[0]);
  ebur128_loudness_momentary(st, &results[1]);
  ebur128_loudness_shortterm(st, &results[2]);
  ebur128_loudness_range(st, &results[3]);
  ebur128_relative_threshold(st, &results[4]);
  ebur128_loudness_window(st, 400, &results[5]);
  Write1D("extreme_results", results, 6);

  double *peaks = (double *)malloc(sizeof(double) * channels * 4);
  for (unsigned int c = 0; c < channels; ++c) {
    double value;
    ebur128_sample_peak(st, c, &value);
    peaks[c * 4] = value;
    ebur128_true_peak(st, c, &value);
    peaks[c * 4 + 1] = value;
    ebur128_prev_sample_peak(st, c, &value);
    peaks[c * 4 + 2] = value;
    ebur128_prev_true_peak(st, c, &value);
    peaks[c * 4 + 3] = value;
  }
  Write2D("extreme_peaks", peaks, (int)channels, 4);
  free(peaks);

  DumpBlockList("extreme_blocks", &st->d->block_list);
  DumpBlockList("extreme_shortterm", &st->d->short_term_block_list);

  double state[10];
  for (unsigned int c = 0; c < channels; ++c) {
    for (int j = 0; j < FILTER_STATE_SIZE; ++j) {
      state[c * FILTER_STATE_SIZE + j] = st->d->v[c][j];
    }
  }
  Write2D("extreme_state", state, (int)channels, FILTER_STATE_SIZE);

  ebur128_destroy(&st);
  free(input);
}

static void DumpMultiple(void) {
  const unsigned int channels = 2;
  const unsigned long fs = 48000;
  const int seconds = 12;
  const size_t chunkFrames = 4801;
  const int parts = 3;

  size_t frames = (size_t)fs * (size_t)seconds;
  double *input = BuildLoudnessInput(channels, fs, frames);

  ebur128_state *states[3];
  int mode = EBUR128_MODE_I | EBUR128_MODE_LRA;

  size_t span = frames / (size_t)parts;
  for (int p = 0; p < parts; ++p) {
    states[p] = ebur128_init(channels, fs, mode);
    if (states[p] == NULL) {
      printf("ebur128_init failed for the multiple dump\n");
      exit(1);
    }

    size_t begin = (size_t)p * span;
    size_t end = p + 1 == parts ? frames : begin + span;
    for (size_t offset = begin; offset < end; offset += chunkFrames) {
      size_t take = end - offset;
      if (take > chunkFrames) {
        take = chunkFrames;
      }
      ebur128_add_frames_double(states[p], input + offset * channels, take);
    }
  }

  double results[4];
  results[0] = (double)ebur128_loudness_global_multiple(states, parts, &results[1]);
  results[2] = (double)ebur128_loudness_range_multiple(states, parts, &results[3]);
  Write1D("multiple_results", results, 4);

  double single[6];
  for (int p = 0; p < parts; ++p) {
    ebur128_loudness_global(states[p], &single[p * 2]);
    ebur128_loudness_range(states[p], &single[(p * 2) + 1]);
  }
  Write2D("multiple_singles", single, parts, 2);

  double counts[3];
  for (int p = 0; p < parts; ++p) {
    counts[p] = (double)states[p]->d->block_list_size;
  }
  Write1D("multiple_counts", counts, parts);

  for (int p = 0; p < parts; ++p) {
    ebur128_destroy(&states[p]);
  }
  free(input);
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
  DumpInterpolator();

  {
    static const int surround[5] = {
      EBUR128_LEFT, EBUR128_RIGHT, EBUR128_CENTER,
      EBUR128_LEFT_SURROUND, EBUR128_RIGHT_SURROUND
    };
    static const int dualMono[1] = { EBUR128_DUAL_MONO };
    int full = EBUR128_MODE_I | EBUR128_MODE_LRA | EBUR128_MODE_TRUE_PEAK;

    DumpLoudness("stereo", 2, 48000, 20, full, NULL, 4801);
    DumpLoudness("surround", 5, 48000, 20, full, surround, 4801);
    DumpLoudness("dualmono", 1, 48000, 20, full, dualMono, 4801);
    DumpLoudness("histogram", 2, 48000, 20, full | EBUR128_MODE_HISTOGRAM,
        NULL, 4801);
    DumpLoudness("aligned", 2, 48000, 20, full, NULL, 4800);
    DumpLoudness("rate44100", 2, 44100, 20, full, NULL, 4801);
    DumpLoudness("rate96000", 2, 96000, 12, full, NULL, 4801);
  }

  DumpExtreme();
  DumpMultiple();

  printf("reference data written to %s\n", g_outdir);
  return 0;
}

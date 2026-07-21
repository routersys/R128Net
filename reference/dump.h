#ifndef R128NET_REFERENCE_DUMP_H_
#define R128NET_REFERENCE_DUMP_H_

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

extern char g_outdir[1024];

static void WriteDump(const char *name, const int *dims, int ndim,
    const double *data) {
  char path[2048];
  snprintf(path, sizeof(path), "%s/%s.bin", g_outdir, name);
  FILE *fp = fopen(path, "wb");
  if (fp == NULL) {
    printf("cannot open %s\n", path);
    exit(1);
  }
  int32_t nd = ndim;
  fwrite(&nd, sizeof(int32_t), 1, fp);
  for (int i = 0; i < ndim; ++i) {
    int32_t d = dims[i];
    fwrite(&d, sizeof(int32_t), 1, fp);
  }
  size_t total = 1;
  for (int i = 0; i < ndim; ++i) total *= (size_t)dims[i];
  fwrite(data, sizeof(double), total, fp);
  fclose(fp);
}

static void Write1D(const char *name, const double *data, int n) {
  int dims[1];
  dims[0] = n;
  WriteDump(name, dims, 1, data);
}

static void Write2D(const char *name, const double *data, int rows, int cols) {
  int dims[2];
  dims[0] = rows;
  dims[1] = cols;
  WriteDump(name, dims, 2, data);
}

static void WriteFloats(const char *name, const float *data, int n) {
  double *flat = (double *)malloc(sizeof(double) * (size_t)n);
  for (int i = 0; i < n; ++i) flat[i] = (double)data[i];
  Write1D(name, flat, n);
  free(flat);
}

static void WriteInts(const char *name, const int *data, int n) {
  double *flat = (double *)malloc(sizeof(double) * (size_t)n);
  for (int i = 0; i < n; ++i) flat[i] = (double)data[i];
  Write1D(name, flat, n);
  free(flat);
}

static void WriteUnsigned(const char *name, const unsigned int *data, int n) {
  double *flat = (double *)malloc(sizeof(double) * (size_t)n);
  for (int i = 0; i < n; ++i) flat[i] = (double)data[i];
  Write1D(name, flat, n);
  free(flat);
}

static void WriteScalar(const char *name, double value) {
  Write1D(name, &value, 1);
}

#endif  /* R128NET_REFERENCE_DUMP_H_ */

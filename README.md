# R128Net

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](#)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](#)
[![Release](https://img.shields.io/github/v/release/routersys/R128Net.svg)](https://github.com/routersys/R128Net/releases)

English | [日本語](https://github.com/routersys/R128Net/blob/main/README.ja.md)

---

A complete C# port of [libebur128](https://github.com/jiixyj/libebur128), the loudness measurement library implementing EBU R128 and ITU-R BS.1770 by Jan Kokemüller.
Measurement runs without a single managed allocation, so the garbage collector never observes the analysis path.
All state comes from one native block, every hot routine is written with `unsafe` pointers, and the whole library is annotated for Native AOT.
Correctness is not asserted from reading the source: every stage is compared against golden data produced by the original C compiled with MSVC.

---

## Table of Contents

1. [Overview](#overview)
2. [Requirements](#requirements)
3. [Installation](#installation)
4. [Features](#features)
   - [1. Loudness measurement](#1-loudness-measurement)
   - [2. Peak measurement](#2-peak-measurement)
   - [3. Gating and the histogram](#3-gating-and-the-histogram)
   - [4. Multiple instances](#4-multiple-instances)
   - [5. Zero allocation and the state buffer](#5-zero-allocation-and-the-state-buffer)
   - [6. Numerical verification](#6-numerical-verification)
   - [7. Performance](#7-performance)
5. [API Reference](#api-reference)
   - [Construction](#construction)
   - [Feeding audio](#feeding-audio)
   - [Loudness](#loudness)
   - [Peaks](#peaks)
   - [Channels](#channels)
   - [Options](#options)
6. [Limitations](#limitations)
7. [Notes](#notes)
8. [Reporting issues](#reporting-issues)
9. [Disclaimer](#disclaimer)
10. [Third-Party Licenses](#third-party-licenses)
11. [License](#license)

---

## Overview

R128Net ports libebur128 to C#. Momentary, short-term and integrated loudness, loudness range, the relative threshold, sample peak and true peak are all provided, together with the histogram algorithm and the aggregation of several measurement instances into a single figure.

The public surface is modern C#. Audio is passed as `ReadOnlySpan<short>`, `ReadOnlySpan<int>`, `ReadOnlySpan<float>` or `ReadOnlySpan<double>`, the mode set is a `[Flags]` enum that reproduces the implied bits of the original, options are a `readonly record struct` with `init` accessors, and results are properties on a single `LoudnessMeter` class. The pointer-based internals are not exposed.

All state is served by one 64-byte aligned block obtained from `NativeMemory.AlignedAlloc` at construction. The layout of that block is declared once as a `Layout` method on a dedicated type; a source generator reads that method's signature and emits both the size query and the binding call, so the reported size and the actual consumption cannot drift apart.

The original C is not vendored into this repository. The reference harness under `reference/` clones libebur128 at a pinned commit, builds it with MSVC and dumps the internal state of every stage as raw doubles. The test suite loads those dumps and compares them against the C# results.

---

## Requirements

| Item | Requirement |
|---|---|
| OS | Windows, Linux or macOS supported by .NET 10 |
| SDK | .NET SDK 10.0 |
| Language | C# 14 or later (`LangVersion` is set to `latest`) |
| Unsafe code | Not required in the consuming project |
| Reference data | MSVC C toolset and Git, required only to regenerate the golden data used by the tests |

---

## Installation

```sh
dotnet add package R128Net
```

1. Create a `LoudnessMeter` with the channel count, the sample rate and the modes you need. Request the lowest set of modes that suits your needs, because every mode costs processing time.
2. Feed interleaved audio through `AddFrames`. Buffers of any length are accepted as long as they hold a whole number of frames.
3. Read the results as properties. They may be read at any point during the measurement.
4. The reference data that the test suite compares against is committed under `reference/data`, so the suite runs right after a clone. To regenerate it, execute `reference/build.bat`, which clones libebur128, builds it with MSVC and overwrites the dumps.
5. To produce a Native AOT binary of the sample application, run `publish-aot.bat`.

---

## Features

### 1. Loudness measurement

The BS.1770 pre-filter is the cascade of a high shelf and a high pass, combined into one fourth-order section exactly as the original does. Gating blocks span 400 ms and advance by 100 ms, giving the 75 percent overlap of the 2011 revision.

Momentary loudness covers the last 400 ms and short-term loudness the last 3 s. Integrated loudness applies the absolute gate at -70 LUFS and then the relative gate 10 LU below the ungated mean. Loudness range follows EBU Tech 3342, taking the 10th and 95th percentile of the short-term blocks that survive a gate 20 dB below their mean. `GetLoudnessOverWindow` reports the loudness of any window up to the configured maximum.

### 2. Peak measurement

Sample peak is the largest absolute sample divided by the scaling factor of the input format. True peak oversamples by four below 96 kHz and by two below 192 kHz using a 49-tap polyphase FIR interpolator, and leaves the signal unchanged at 192 kHz and above. Both are available for the whole measurement and for the most recent call to `AddFrames`.

The original writes the converted input to one buffer, writes every interpolated sample to a second buffer, and scans that buffer for the peak in a separate pass. Both buffers and both passes are fused away here. The stored value would be read back unchanged, and a maximum does not depend on the order of the scan, so the fusion leaves every result identical.

### 3. Gating and the histogram

Channel weights follow BS.1770: the surround positions contribute a factor of 1.41, a channel marked as dual mono contributes a factor of two, and a channel marked as unused is skipped and is not written to the ring buffer at all.

The histogram algorithm quantises block energies into 1000 bins of 0.1 LU and is selected with `LoudnessModes.Histogram`. It bounds the memory of a long measurement at the cost of quantising the result. Without it the block energies are kept individually, which is the default and matches the original.

### 4. Multiple instances

`LoudnessMeter.GatedLoudness` and `LoudnessMeter.LoudnessRangeOf` accept a span of meters and produce the figure that a single meter fed with all the material would have produced. This is the intended way to measure several files as one programme, or to measure one file on several threads. The meters must agree on whether they use the histogram algorithm.

### 5. Zero allocation and the state buffer

The filtered ring buffer, the filter state, the channel map, the peak arrays, the histograms and the interpolator all live in one aligned native block allocated at construction. A type marked with `[StateLayout]` declares its requirement once as a `Layout` method generic over an allocator. Running it with the measuring allocator yields the required byte count without touching memory, and running it with the binding allocator performs the actual binding. The source generator emits `GetRequiredBytes` and `Bind` from that single signature.

The block energy history grows geometrically in native memory by default, which reproduces the effectively unbounded history of the original. `PreallocateHistory` sizes it once from the requested maximum history instead, together with the scratch that loudness range sorts into, after which neither measuring nor querying a single meter allocates anything. `LoudnessMeter.LoudnessRangeOf` over several meters sorts the blocks of all of them in the scratch of the first meter, so it allocates native memory once when the combined count exceeds what that meter reserved, and keeps the larger scratch for later queries.

`Reset` returns a meter to its as-constructed measurement state while keeping the channel map, the window and the history. Measuring a sequence of candidates therefore costs one construction rather than one per candidate, which is the supported way to reuse a meter.

The absence of managed allocation is measured, not asserted. `GC.GetAllocatedBytesForCurrentThread` reports a delta of zero bytes across `AddFrames` for all four input formats and across every query, including the sort that loudness range performs.

### 6. Numerical verification

The test suite compares against dumps produced by the original C built with MSVC. Because the internal state of libebur128 is an incomplete type, the harness includes `ebur128.c` into its own translation unit and reaches the state directly. The following table records the agreement that the tests enforce.

| Stage | Agreement |
|---|---|
| Pre-filter coefficients, 20 sample rates | Bit-exact |
| Filtered output and filter state | Bit-exact |
| Filter state through a silent decay, 40 blocks | Bit-exact |
| Interpolator coefficients, both oversampling factors | Bit-exact |
| Gating block energies and short-term block energies | Bit-exact |
| Integrated loudness, momentary, short-term, window, relative threshold | Bit-exact |
| Loudness range, list algorithm and histogram algorithm | Bit-exact |
| Sample peak and true peak, current and previous | Bit-exact |
| Histogram bin counts, 1000 bins of each kind | Bit-exact |
| Aggregation across three instances | Bit-exact |

Those figures hold for eight configurations: stereo, five-channel surround, dual mono, the histogram algorithm, buffer lengths aligned and unaligned to the 100 ms boundary, 44100 Hz, 96000 Hz, and an adversarial signal whose amplitude cycles through 1e-310, 1e-40, 5e-324 and exact zero.

The transcendental functions are measured separately. `Math.Tan` and `Math.Log` return exactly the same doubles as the MSVC runtime over 20000 sampled arguments each. `Math.Pow` differs by at most one unit in the last place on fewer than one in a thousand of the sampled inputs, which is why the histogram boundary table is embedded rather than computed.

The suite contains 270 tests and all of them pass. Beyond the comparison against the original, they cover the disposal and mode contract of every public member, the validation of every argument, the requirement that a meter which has been reset produce results identical to a freshly constructed one across all of the configurations above, and the agreement of every fast path with a plain reference implementation.

### 7. Performance

The tables compare the original C compiled with MSVC at `/O2` against this port published with Native AOT. Both are compiled ahead of time, so the comparison is like for like. Each table measures both builds back to back in one session, so the ratio is the meaningful quantity while the absolute values move with the machine.

The first table was taken on a workstation for the current implementation. It is not regenerated.

Intel Core i7-1360P under Windows 11, sample application published for an `x86-64-v3` baseline, best of 21 runs in milliseconds, analysing 30 seconds of stereo 48 kHz audio in 100 ms buffers. The builds ran alternately and every process was pinned to the performance cores, because this processor mixes core types and the sample application ran up to 1.6 times slower on the efficiency cores.

| Mode set | C with MSVC | This port with Native AOT | Ratio |
|---|---:|---:|---:|
| Momentary only | 27.15 | 6.50 | 4.18x |
| Integrated | 34.14 | 10.86 | 3.14x |
| Loudness range | 32.83 | 10.52 | 3.12x |
| Integrated and sample peak | 38.90 | 11.71 | 3.32x |
| Integrated and true peak | 213.12 | 20.19 | 10.55x |
| Every mode | 210.94 | 23.55 | 8.96x |

The second table is regenerated by the benchmark workflow on a GitHub Actions runner. The hardware differs from the workstation, which makes it an independent check that the ratios do not depend on one particular machine.

<!-- BENCHMARK:CI:BEGIN -->

Measured by CI on a GitHub Actions `windows-latest` runner with AMD EPYC 9V74 80-Core Processor. Figures are the best of 20 runs in milliseconds, analysing 30 seconds of stereo 48 kHz audio in 100 ms buffers. Both builds run back to back in the same job, so the ratio is the stable quantity; the absolute values move with the shared runner. Recorded on 2026-07-21 from commit `868c9f2`.

| Mode set | C with MSVC | This port with Native AOT | Ratio |
|---|---:|---:|---:|
| Momentary only | 32.86 | 34.69 | 0.95x |
| Integrated | 44.56 | 46.85 | 0.95x |
| Loudness range | 40.94 | 43.27 | 0.95x |
| Integrated and sample peak | 47.22 | 47.72 | 0.99x |
| Integrated and true peak | 172.37 | 172.98 | 1.00x |
| Every mode | 180.86 | 182.27 | 0.99x |

<!-- BENCHMARK:CI:END -->

A ratio above 1.00 means this port is faster than the original C. The second table records the commit it was measured from, which predates the optimisations below, and is replaced the next time the workflow runs.

True peak is the largest cost, at about two fifths of the every-mode time in the stereo measurement above. Every optimisation below leaves the output bit-exact. The interpolator and the filter are each checked against a reference implementation that applies the flush after every operation, as the original does through the MXCSR register, and gating is checked against the scalar sum of the original.

| Optimisation | Effect |
|---|---|
| Delay line held already widened to double, removing a conversion per tap | 1.5x |
| Flushes on the products and sums of the interpolator taps removed, because they are the identity | 1.4x on true peak |
| Absolute value of an interpolated sample taken without a branch | 1.7x on true peak |
| Peak tracked as a magnitude per lane and narrowed to single precision once per call | 1.1x on every mode |
| Four consecutive frames placed in the lanes of one vector, with the three phases as separate accumulators, over a contiguous history | 1.3x on every mode at two channels |
| Eight frames checked in single precision against a proven bound, and the double precision computation skipped when none of them can exceed the largest output already seen | 1.4x on every mode at two channels, 1.7x on true peak alone |
| Windows of zeros passed by the check at once, so that digital silence costs almost nothing | 1.9x on true peak for digital silence |
| Input converted to the interpolator's single precision history eight samples at a time for one and two channels | 1.1x on every mode for double precision input, 1.2x for 16-bit integers |
| K-weighting filter without per-operation flush while the state is settled | 4.0x on the filter |
| Gating sum computed with one channel per vector lane | 1.25x on every mode at two channels |
| Absolute value in the scalar sample peak taken without a branch | 3.0x on the sample peak mode at six channels |
| Sample peak accumulated with one absolute value and one maximum per vector, in integer arithmetic for integer input | 1.1x to 1.2x on every mode for 16-bit input and at six channels |
| Unused channels skipped when the filter forms vector groups | 1.35x on the filter at six channels |

The first row was measured in an earlier stage of the work. The others were measured one at a time against the commit before each change, with the two builds alternating under Native AOT and the processes pinned as above. They are ratios of the best of 13 to 21 runs, and two copies of the same build differed by up to about 10 percent depending on the stage, so the small ratios carry that uncertainty. Taken together, under Native AOT the original port and the current one differ by 8.8x on every mode at two channels, 8.9x at six channels with the default layout and 5.9x at one channel; under the just-in-time compiler, including the compilation on the first call, the same comparison gives 6.8x, 9.1x and 5.4x. The output of every measured configuration is identical to the bit. An earlier structure of the dense interpolation, with a delay line doubled in length, two frames unrolled and the three phases held in the lanes of one vector, was replaced by the last structure above. The general path, which serves the sample rates for which the subfilters are not dense, keeps the doubled delay line.

The flushes on the interpolator taps are the identity for a reason that can be stated exactly. The history holds only zero or values of at least 2^-126 in magnitude, because each sample is narrowed to single precision and flushed there first, and every coefficient the interpolator keeps exceeds 10^-6. Every product is therefore at least 2^-146, and because each partial sum is a multiple of 2^-198, every nonzero partial sum is at least 2^-198, far above the smallest normal double 2^-1022. Narrowing to single precision and the flush are both monotonic, so the largest of several magnitudes can be narrowed once instead of narrowing each; the peak is therefore tracked in double precision and converted once per call.

Each accumulation chain must keep the addition order of the original, so a chain cannot be shortened. The speed comes from computing independent outputs side by side: the three phases of one frame no longer share a vector with an idle lane, and four frames advance together.

The interpolator avoids most of its double precision work by a proof. Each output is a sum of twelve products of history samples and coefficients, and the history holds single precision values widened to double. For eight frames at once, the four central taps of each of the three phases are summed in single precision, and the other eight taps are bounded by the sum of their coefficient magnitudes times the largest magnitude among the twenty samples that the eight frames read. A margin of 2x10^-5 of that largest magnitude is added. The rounding error of the single precision sum is at most about 10^-6 of it, so the margin is twenty times larger than needed; it also covers the rounding error of the double precision result and the flushing of denormals that a thread might have enabled. If the bound on every output of the eight frames, including the sample itself at the centre, is at most 0.99999 times the largest output seen so far, or the peak left by earlier calls, none of them can change the maximum and the exact computation of the eight frames is skipped. An output can exceed that largest value only rarely, so the check passed for 99.5 percent of the blocks of uniform noise, 98.8 percent of those of a music-like signal and 97 percent of those of clipped noise. A window of zeros passes outright, because every output is then exactly zero. The remaining blocks take the exact path unchanged, so the result does not depend on how often the check passes.

The soundness of the check is tested directly instead of through the final result, which is narrowed to single precision and would hide an excess smaller than that resolution. For hundreds of thousands of windows of many shapes (noise, isolated spikes, sinusoids, the sign pattern that maximises an output, constants, windows with not a number or an infinity, denormals, and magnitudes that span 10^-35 to 3x10^38), the check is run against thresholds from 1 percent below to far above the true largest output, and it must never pass when the largest output exceeds the threshold. The same test fails for each of a series of deliberately broken versions: all the margins removed together, a halved tail bound, a window that misses its oldest or newest sample, an unchecked centre sample, and a threshold scale above one.

The conversion of the input to the history is done eight samples at a time for one and two channels, and the interleaved channels of the two-channel case are separated with shuffles. The 16-bit and 32-bit integer formats are converted exactly: scaling by a power of two commutes with the rounding to single precision, so the vector result equals the scalar definition bit for bit. The only difference is the payload of a signalling not a number, which the arithmetic turns into a quiet one and which cannot influence a maximum.

The K-weighting filter has a fast path that performs no flush while the input and the four states are each zero or at least 2^-900 in magnitude. Under that condition, and with every nonzero coefficient at least 2^-60 in magnitude, no intermediate value of a sample can be denormal, so the flush would change nothing. The smallest nonzero coefficient measured over every integer sample rate up to 200 kHz was 6.8e-7. The input and the new state are checked on every sample; the first sample that fails the check is computed with the flush after every operation, and so is every sample after it until the states are settled again. After a decay into silence the states stay below 2^-900, so digital silence that follows audio remains on the slow path: there the filter measured 1.6x faster than the original port and every mode 2.1x faster, against 3.5x and 5.6x on noise.

The filter forms groups of four adjacent channels in use for 256-bit vectors, then groups of two for 128-bit vectors, and processes the rest one channel at a time. Each lane performs the arithmetic of one channel in the original order, which is bit-exact. A layout with an unused channel, such as 5.1 with the LFE channel unused, still forms a group for each pair of adjacent channels that are in use. Gating visits the samples in the ring-buffer order of the original, so each sum stays one sequential chain, and the channels are summed in parallel, one per vector lane.

Sample peak is vectorised across the interleaved buffer whenever the pattern of channels repeats within three vectors, which covers one to four channels, six and eight. A maximum does not depend on the order in which it is taken, so the result is bit-exact, and the comparison reproduces the ternary of the original including its treatment of negative zero and of not a number. Integer input is handled in integer arithmetic, where the absolute value of the most negative number is representable as an unsigned number. Other channel counts take a scalar loop with a branch-free absolute value.

The kernels that contain loops carry `AggressiveOptimization`, so under the just-in-time compiler they are compiled with full optimisation on the first call instead of starting in the unoptimised tier. Native AOT compiles ahead of time and is unaffected.

---

## API Reference

### Construction

```csharp
using R128Net;

using LoudnessMeter meter = new(channels: 2, sampleRate: 48000, LoudnessModes.All);
```

| Member | Description |
|---|---|
| `LoudnessMeter(int, int, LoudnessModes)` | Creates a meter with the default options |
| `LoudnessMeter(int, int, LoudnessModes, in LoudnessMeterOptions)` | Creates a meter with explicit options |
| `Channels`, `SampleRate`, `Modes` | The configuration the meter was created with |
| `MaxWindowMilliseconds`, `MaxHistoryMilliseconds` | The current window and history |
| `FramesProcessed` | Frames fed since construction or since the last `Reset` |
| `Reset` | Clears the measurement and keeps the configuration |
| `SetMaxHistory(long)` | Shortens or lengthens the retained history |
| `Dispose` | Releases the native state |

`LoudnessModes` reproduces the implied bits of the original: `ShortTerm` implies `Momentary`, `Integrated` implies `Momentary`, `LoudnessRange` implies `ShortTerm`, and `TruePeak` implies `SamplePeak`.

### Feeding audio

```csharp
meter.AddFrames(interleaved);
```

| Overload | Full-scale value |
|---|---|
| `AddFrames(ReadOnlySpan<short>)` | 32768 |
| `AddFrames(ReadOnlySpan<int>)` | 2147483648 |
| `AddFrames(ReadOnlySpan<float>)` | 1.0 |
| `AddFrames(ReadOnlySpan<double>)` | 1.0 |

Samples are interleaved by channel. The span must hold a whole number of frames.

### Loudness

| Member | Unit | Requires |
|---|---|---|
| `MomentaryLoudness` | LUFS | `Momentary` |
| `ShortTermLoudness` | LUFS | `ShortTerm` |
| `IntegratedLoudness` | LUFS | `Integrated` |
| `LoudnessRange` | LU | `LoudnessRange` |
| `RelativeThreshold` | LUFS | `Integrated` |
| `GetLoudnessOverWindow(long)` | LUFS | `Momentary` |
| `LoudnessMeter.GatedLoudness(ReadOnlySpan<LoudnessMeter>)` | LUFS | `Integrated` on every meter |
| `LoudnessMeter.LoudnessRangeOf(ReadOnlySpan<LoudnessMeter>)` | LU | `LoudnessRange` on every meter |

A loudness of negative infinity means the material is silent or entirely below the absolute gate. A loudness range of zero means too few short-term blocks survived the gate.

### Peaks

| Member | Requires |
|---|---|
| `GetSamplePeak(int)` | `SamplePeak` |
| `GetPreviousSamplePeak(int)` | `SamplePeak` |
| `GetTruePeak(int)` | `TruePeak` |
| `GetPreviousTruePeak(int)` | `TruePeak` |

A value of 1.0 is 0 dBFS, so the conversion to decibels is `20 * log10(value)`. The previous variants report the peak of the most recent call to `AddFrames`. True peak never reports less than sample peak.

### Channels

| Member | Description |
|---|---|
| `SetChannel(int, ChannelPosition)` | Assigns a position to a channel |
| `GetChannel(int)` | Reads the position of a channel |

The default map assigns the BS.1770 layout for four and five channels, and otherwise assigns left, right, centre, unused, left surround and right surround from the first channel onwards. `ChannelPosition.DualMono` applies only to the single channel of a mono meter.

### Options

| Member | Default | Description |
|---|---|---|
| `MaxWindowMilliseconds` | 0 | The longest window `GetLoudnessOverWindow` may request. Zero means the minimum the requested modes need, which is 3000 with short term and 400 without. A window whose frame count does not fit, or wraps to fewer frames than 400 milliseconds, is rejected with `ArgumentOutOfRangeException` |
| `MaxHistoryMilliseconds` | 4294967295 | The history retained for integrated loudness and loudness range |
| `PreallocateHistory` | `false` | Sizes the history once instead of growing it. With the default `MaxHistoryMilliseconds` this reserves about 350 MB at construction, so set a history of the length that is needed |
| `UseUpstreamWindowOverflow` | `false` | Reproduces the integer overflow of the original on Windows |

---

## Limitations

- Bit-exactness against libebur128 compiled with MSVC is verified on Windows x64, where the .NET runtime and the reference share the same math library. CI also runs the whole suite on Windows ARM64, Linux x64 and Linux ARM64 against the same dumps, and every comparison passes bit for bit there except the four that call the math library of the platform directly: `Math.Tan`, `Math.Log`, and the interpolator and K-weighting coefficients computed from the trigonometric functions. On those three platforms they were measured to differ from the MSVC runtime by one unit in the last place at isolated arguments, and by up to three units for one K-weighting coefficient at 8000 Hz on Windows ARM64, so the tests allow one unit for `Math.Tan`, `Math.Log` and the interpolator coefficients, and four units for the K-weighting coefficients, there. A measurement can therefore differ in the last bits on another platform when a sample rate happens to hit such an argument; the loudness and peak tests of the suite did not.
- The histogram boundary table differs from the original at two of its 1001 entries by one unit in the last place. The embedded values are the correctly rounded ones; the MSVC `pow` is not, and `pow` is not required to be correctly rounded by IEEE 754. The difference is confined to the histogram algorithm.
- `LoudnessMeter` is not thread-safe. Concurrent measurement requires one meter per thread, which the aggregation functions are designed for.
- Sample rates at which the pre-filter diverges are rejected at construction rather than accepted as the original accepts them. Every rate from 3364 Hz upwards is accepted.
- `ebur128_set_max_window` is replaced by `LoudnessMeterOptions.MaxWindowMilliseconds`, which fixes the size at construction rather than reallocating during a measurement. `ebur128_change_parameters` is not ported: it reallocates every buffer and discards the measurement, which is what constructing a new meter already does, and the upstream implementation of it is the one carrying the integer overflow described above.
- Raising `MaxWindowMilliseconds` above the default for the requested modes changes how often the ring buffer wraps. The upstream summation visits the two halves of a wrapped block in the opposite order to an unwrapped one, so gated results move by a few units in the last place; four units were measured over twelve seconds of stereo. Peaks are unaffected, and the default window reproduces the original exactly.
- Running the comparison tests requires the reference data. Without `reference/data` those tests cannot execute.
- The sample application under `R128Net.Examples` writes to the console and therefore allocates managed memory. The zero-allocation guarantee applies to the library.

---

## Notes

- Processing cost: true peak is the most expensive mode, accounting for about half of the total in the stereo measurement above. Omitting it from the mode set is the single most effective way to speed up a measurement. Sample peak and gating are minor by comparison.
- Denormal handling across architectures: in the source of the original the flush-to-zero bit is enabled only where SSE2 is used, and elsewhere the state is flushed by hand once per call, which differs for denormal values. The port reproduces the x86-64 behaviour on every architecture, so its results are identical on all of them, and the suite confirms this against the dumps made on x86-64. It may differ from a native build of the original on another architecture for denormal values only. This was derived from reading the source and was not compared against such a build.
- Denormal handling: the original enables the flush-to-zero bit of the MXCSR register while filtering. The .NET runtime exposes no equivalent, so the flush is emulated at the level of the individual operation, and left out where it is provably the identity. The emulation reproduces the state trajectory of the original through a silent decay exactly, and it also avoids the fifty-fold slowdown that denormal arithmetic would otherwise cause on silence.
- Meter reuse: the state is allocated once at construction. Creating a meter for every buffer defeats the purpose and reintroduces native allocation. Call `Reset` between candidates instead; it clears the measurement without touching the configuration and without allocating.
- Determinism: the measurement produces identical output across repeated runs and does not depend on the length of the buffers passed to `AddFrames`. The test suite feeds the same material one frame at a time and in bulk and compares the results bit for bit.
- State layout: a type marked with `[StateLayout]` must expose a `Layout` method generic over `IStateAllocator`. The generator emits `GetRequiredBytes` and `Bind` with a matching parameter list, and skips whichever of the two the type already declares.
- Native AOT: the library sets `IsAotCompatible`, which enables the trim, single-file and AOT analyzers. `publish-aot.bat` publishes the sample application for `win-x64` and requires the MSVC toolset for the native linker. It also places the Visual Studio installer directory on the path, because the linker probe of the AOT compiler fails when `vswhere.exe` cannot be resolved.
- Regenerating reference data: `reference/build.bat` clones libebur128 into `reference/ebur128-src`, verifies that the checkout is the pinned commit, builds it, and writes the dumps. The clone and the build output are excluded from version control.

---

## Reporting issues

Report problems on GitHub [Issues](https://github.com/routersys/R128Net/issues). The library does not send any report automatically.

Please include the following in the report.

| Item | Content |
|---|---|
| Environment | The version of R128Net, the version of the .NET SDK or runtime, the operating system and the processor architecture |
| Input | The sample format, the number of channels, the sampling rate and the number of frames, the length of each `AddFrames` call, and the modes requested |
| Steps | The calls in the order they were made, including every option that differs from its default |
| Result | The expected result and the actual result. For a numerical difference, the quantity and both values |
| Exception | The type, the message and the stack trace, unmodified |

Report a vulnerability privately as the [security policy](https://github.com/routersys/R128Net/blob/main/SECURITY.md) describes, not in an issue.

To contribute a change, read the [contributing guide](https://github.com/routersys/R128Net/blob/main/CONTRIBUTING.md).

---

## Disclaimer

This library is published under the MIT License.

This software is provided "as is", without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose and noninfringement.

The author accepts no liability for any damage arising from the use of or the inability to use this library. Use it at your own risk.

---

## Third-Party Licenses

R128Net is a derivative work of the software below. The full license text is stored in the repository under [`.github/LICENSE/libebur128.txt`](https://github.com/routersys/R128Net/blob/main/.github/LICENSE/libebur128.txt).

libebur128 is distributed under the MIT License, which requires that redistributions retain its copyright notice and permission notice. That file carries the text unmodified. No third-party source code is vendored into this repository, and the reference harness downloads libebur128 on demand.

| Software | Purpose | License | Copyright |
|---|---|---|---|
| [libebur128](https://github.com/jiixyj/libebur128) | Origin of every ported algorithm and of the reference implementation used for verification | MIT License | Copyright (c) 2011 Jan Kokemüller |

---

## License

[MIT License](https://github.com/routersys/R128Net/blob/main/LICENSE.txt)

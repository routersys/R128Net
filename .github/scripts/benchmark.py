import argparse
import os
import platform
import subprocess
import sys
from datetime import datetime, timezone

STAGES = [
    "Momentary",
    "Integrated",
    "LoudnessRange",
    "SamplePeak",
    "TruePeak",
    "All",
]

LABELS = {
    "Momentary": ("Momentary only", "瞬時値のみ"),
    "Integrated": ("Integrated", "統合値"),
    "LoudnessRange": ("Loudness range", "ラウドネスレンジ"),
    "SamplePeak": ("Integrated and sample peak", "統合値とサンプルピーク"),
    "TruePeak": ("Integrated and true peak", "統合値とトゥルーピーク"),
    "All": ("Every mode", "全モード"),
}

BEGIN = "<!-- BENCHMARK:CI:BEGIN -->"
END = "<!-- BENCHMARK:CI:END -->"


def run_once(command, environment):
    completed = subprocess.run(command, capture_output=True, text=True, env=environment)
    if completed.returncode != 0:
        sys.stderr.write(completed.stdout)
        sys.stderr.write(completed.stderr)
        raise SystemExit(f"benchmark command failed: {' '.join(command)}")
    values = {}
    for line in completed.stdout.splitlines():
        parts = line.split()
        if len(parts) == 3 and parts[0] == "BENCH":
            values[parts[1]] = float(parts[2])
    return values


def measure(command, environment, runs):
    best = {}
    for _ in range(runs):
        for stage, value in run_once(command, environment).items():
            if stage not in best or value < best[stage]:
                best[stage] = value
    missing = [stage for stage in STAGES if stage not in best]
    if missing:
        raise SystemExit(f"missing stages from {' '.join(command)}: {', '.join(missing)}")
    return best


def processor_name():
    try:
        completed = subprocess.run(
            ["powershell", "-NoProfile", "-Command",
             "(Get-CimInstance Win32_Processor).Name"],
            capture_output=True, text=True)
        lines = [line.strip() for line in completed.stdout.splitlines() if line.strip()]
        if lines:
            return lines[0]
    except OSError:
        pass
    return platform.processor() or "an unidentified processor"


def rows(native, aot, jit):
    for stage in STAGES:
        yield (stage, native[stage], aot[stage], native[stage] / aot[stage],
               jit[stage], native[stage] / jit[stage])


def english_block(native, aot, jit, runs, cpu, stamp, commit):
    lines = [
        BEGIN,
        "",
        f"Measured by CI on a GitHub Actions `windows-latest` runner with {cpu}. "
        f"Figures are the best of {runs} runs in milliseconds, analysing 30 seconds of "
        f"stereo 48 kHz audio in 100 ms buffers. Every run of the port is a fresh "
        f"process, so the just-in-time column includes the cost of compiling the code "
        f"and of running it before tiered compilation has optimized it; the six rows "
        f"of one run share a process, so the cost of compiling a routine falls on "
        f"the first row that reaches it. "
        f"All builds run back to back in the same job, so the ratio is the stable "
        f"quantity; the absolute values move with the shared runner. "
        f"Recorded on {stamp} from commit `{commit}`.",
        "",
        "| Mode set | C with MSVC | This port with Native AOT | Ratio "
        "| This port with JIT | Ratio |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for stage, native_value, aot_value, aot_ratio, jit_value, jit_ratio in rows(native, aot, jit):
        lines.append(
            f"| {LABELS[stage][0]} | {native_value:.2f} | {aot_value:.2f} | {aot_ratio:.2f}x "
            f"| {jit_value:.2f} | {jit_ratio:.2f}x |")
    lines.append("")
    lines.append(END)
    return "\n".join(lines)


def japanese_block(native, aot, jit, runs, cpu, stamp, commit):
    lines = [
        BEGIN,
        "",
        f"GitHub Actionsの`windows-latest`ランナー上の{cpu}でCIが計測した値です。"
        f"48 kHzのステレオ音声30秒を100msずつ投入し、{runs}回のうち最良の値をミリ秒で示します。"
        f"本移植は毎回新しいプロセスで実行するので、"
        f"JITの列には、コードのコンパイルと、階層型コンパイルが最適化する前のコードの実行が含まれます。"
        f"1回の実行の6行は1つのプロセスを共有するため、ルーチンのコンパイルの費用は、"
        f"それに最初に達する行に現れます。"
        f"すべてを同一ジョブ内で連続して計測しているため、安定する量は比の方であり、"
        f"絶対値は共有ランナーの状態によって動きます。"
        f"コミット`{commit}`について{stamp}に記録しました。",
        "",
        "| モード集合 | MSVCでビルドしたC | 本移植のNative AOT | 比 | 本移植のJIT | 比 |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for stage, native_value, aot_value, aot_ratio, jit_value, jit_ratio in rows(native, aot, jit):
        lines.append(
            f"| {LABELS[stage][1]} | {native_value:.2f} | {aot_value:.2f} | {aot_ratio:.2f}倍 "
            f"| {jit_value:.2f} | {jit_ratio:.2f}倍 |")
    lines.append("")
    lines.append(END)
    return "\n".join(lines)


def replace(path, block):
    with open(path, encoding="utf-8") as handle:
        text = handle.read()
    begin = text.find(BEGIN)
    end = text.find(END)
    if begin < 0 or end < 0:
        raise SystemExit(f"benchmark markers not found in {path}")
    updated = text[:begin] + block + text[end + len(END):]
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(updated)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--native", required=True)
    parser.add_argument("--aot", required=True)
    parser.add_argument("--jit", required=True)
    parser.add_argument("--runs", type=int, default=20)
    parser.add_argument("--readme", required=True)
    parser.add_argument("--readme-ja", required=True)
    parser.add_argument("--commit", required=True)
    arguments = parser.parse_args()

    native_path = os.path.abspath(arguments.native)
    aot_path = os.path.abspath(arguments.aot)
    jit_path = os.path.abspath(arguments.jit)

    environment = dict(os.environ)
    environment["R128NET_BENCH_ONLY"] = "1"

    native = measure([native_path, "data"], environment, arguments.runs)
    aot = measure([aot_path, "bench"], environment, arguments.runs)
    jit = measure(["dotnet", jit_path, "bench"], environment, arguments.runs)

    cpu = processor_name()
    stamp = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    commit = arguments.commit[:7]

    replace(arguments.readme, english_block(native, aot, jit, arguments.runs, cpu, stamp, commit))
    replace(arguments.readme_ja, japanese_block(native, aot, jit, arguments.runs, cpu, stamp, commit))

    for stage, native_value, aot_value, aot_ratio, jit_value, jit_ratio in rows(native, aot, jit):
        print(f"{stage}: native {native_value:.2f} ms, aot {aot_value:.2f} ms, {aot_ratio:.2f}x, "
              f"jit {jit_value:.2f} ms, {jit_ratio:.2f}x")


if __name__ == "__main__":
    main()

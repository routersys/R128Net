const M = JSON.parse(document.getElementById("messages").textContent);
const $ = (id) => document.getElementById(id);
const fill = (text, values) => text.replace(/\{(\w+)\}/g, (_, key) => values[key]);
const say = (text) => { $("status").textContent = text; };
const MAX_SECONDS = 120;
const DECODE_RATE = 48000;
const STEP_SECONDS = 0.1;
const MODE_COUNT = 6;
const PEAK_LIMIT = -1;

if (!("DecompressionStream" in window) || !("Worker" in window)) {
  say(M.unsupported);
  throw new Error(M.unsupported);
}

const worker = new Worker(new URL("worker.js", import.meta.url), { type: "module" });
const pending = new Map();
let nextId = 0;
let info = null;
let report = null;
let timeline = null;

const CONTROLS = ["source", "file", "preset", "target", "time"];
const enable = (flag) => { for (const id of CONTROLS) $(id).disabled = !flag; };

const call = (name, ...args) => new Promise((resolve, reject) => {
  const id = nextId++;
  pending.set(id, { resolve, reject });
  worker.postMessage({ id, name, args });
});

worker.onmessage = (event) => {
  const data = event.data;
  if (data.progress) {
    const [loaded, total] = data.progress;
    const megabytes = (value) => (value / 1048576).toFixed(1);
    say(total > 0 ? fill(M.loadingOf, { loaded: megabytes(loaded), total: megabytes(total) }) : fill(M.loading, { loaded: megabytes(loaded) }));
  } else if (data.ready) {
    start();
  } else if (data.failed) {
    say(M.fail + data.failed);
  } else if ("id" in data) {
    const entry = pending.get(data.id);
    pending.delete(data.id);
    if (data.error) entry.reject(new Error(data.error));
    else entry.resolve(data);
  }
};

const fail = (error) => say(M.fail + error.message);

const latest = (task) => {
  let running = false;
  let again = false;
  return async () => {
    if (running) { again = true; return; }
    running = true;
    try {
      do { again = false; await task(); } while (again);
    } catch (error) {
      fail(error);
    } finally {
      running = false;
    }
  };
};

const db = (value) => (value > 0 ? 20 * Math.log10(value) : -Infinity);
const fmt = (value, digits = 1) => {
  if (Number.isNaN(value)) return "n/a";
  if (!Number.isFinite(value)) return value < 0 ? "-∞" : "∞";
  return value.toFixed(digits);
};
const withUnit = (value, unit) => (Number.isNaN(value) ? fmt(value) : fmt(value) + " " + unit);
const signed = (value) => {
  const rounded = Math.round(value * 10) / 10;
  return (rounded > 0 ? "+" : "") + fmt(rounded === 0 ? 0 : rounded);
};

const setSource = (id, source) => {
  const audio = $(id);
  if (audio.dataset.url) URL.revokeObjectURL(audio.dataset.url);
  delete audio.dataset.url;
  if (source instanceof Blob) {
    audio.dataset.url = URL.createObjectURL(source);
    audio.src = audio.dataset.url;
  } else {
    audio.src = source;
  }
  audio.hidden = false;
};

const setWave = (id, bytes) => setSource(id, new Blob([bytes], { type: "audio/wav" }));

const layout = (channels) => (channels === 1 ? M.mono : channels === 2 ? M.stereo : fill(M.channels, { n: channels }));

const showInfo = (truncated) => {
  const seconds = (info.frames / info.rate).toFixed(1);
  let text = info.bits > 0
    ? fill(M.audioBits, { layout: layout(info.channels), rate: info.rate, bits: info.bits, seconds })
    : fill(M.audio, { layout: layout(info.channels), rate: info.rate, seconds });
  if (truncated) text += " " + fill(M.truncated, { s: MAX_SECONDS });
  $("audioInfo").textContent = text;
};

const showResults = () => {
  const channels = info.channels;
  const peaks = (offset) => Array.from({ length: channels }, (_, c) => fmt(db(report[offset + c]), 2)).join(", ");
  $("integrated").textContent = fmt(report[0]) + " LUFS";
  $("range").textContent = fmt(report[1]) + " LU";
  $("gate").textContent = fmt(report[2]) + " LUFS";
  $("momentaryMax").textContent = withUnit(report[3], "LUFS");
  $("shortMax").textContent = withUnit(report[4], "LUFS");
  $("samplePeak").textContent = peaks(8) + " dBFS";
  $("truePeak").textContent = peaks(8 + channels) + " dBTP";
  $("measureNote").textContent = fill(M.measured, { seconds: (info.frames / info.rate).toFixed(1), t: report[5].toFixed(0) });
};

const drawTimeline = () => {
  const w = 600, h = 230, left = 40, right = 8, top = 26, bottom = 20;
  const steps = timeline.length / 2;
  const stepSeconds = steps > 0 ? report[7] / info.rate : STEP_SECONDS;
  const finite = Array.from(timeline).filter(Number.isFinite);
  if (finite.length === 0) { $("timeline").textContent = ""; return; }
  let hi = Math.ceil((Math.max(...finite) + 1) / 5) * 5;
  let lo = Math.max(-70, Math.floor(Math.min(...finite) / 5) * 5);
  if (hi - lo < 10) lo = hi - 10;
  const X = (i) => left + (i / Math.max(steps - 1, 1)) * (w - left - right);
  const Y = (v) => top + (1 - (Math.min(Math.max(v, lo), hi) - lo) / (hi - lo)) * (h - top - bottom);
  const path = (offset) => {
    let d = "";
    let pen = false;
    for (let i = 0; i < steps; i++) {
      const v = timeline[2 * i + offset];
      if (Number.isNaN(v)) { pen = false; continue; }
      d += (pen ? "L" : "M") + X(i).toFixed(1) + " " + Y(Number.isFinite(v) ? v : lo).toFixed(1);
      pen = true;
    }
    return d;
  };
  let grid = "";
  for (let v = Math.ceil(lo / 10) * 10; v <= hi; v += 10) {
    grid += '<line x1="' + left + '" x2="' + (w - right) + '" y1="' + Y(v).toFixed(1) + '" y2="' + Y(v).toFixed(1) + '" stroke="currentColor" opacity="0.2"/>'
      + '<text x="' + (left - 4) + '" y="' + (Y(v) + 4).toFixed(1) + '" text-anchor="end">' + v + "</text>";
  }
  const series = [
    { label: M.momentary, d: path(0), width: 1, dash: "" },
    { label: M.shortTerm, d: path(1), width: 2, dash: "6 3" },
  ];
  let body = "";
  if (Number.isFinite(report[0])) {
    const y = Y(report[0]).toFixed(1);
    series.push({ label: M.integrated, d: "M" + left + " " + y + "L" + (w - right) + " " + y, width: 1.5, dash: "2 3" });
  }
  let legend = "";
  series.forEach((s, index) => {
    body += '<path d="' + s.d + '" fill="none" stroke="currentColor" stroke-width="' + s.width + '" stroke-dasharray="' + s.dash + '"/>';
    const x = left + index * 150;
    legend += '<line x1="' + x + '" x2="' + (x + 28) + '" y1="9" y2="9" stroke="currentColor" stroke-width="' + s.width + '" stroke-dasharray="' + s.dash + '"/>'
      + '<text x="' + (x + 33) + '" y="13">' + s.label + "</text>";
  });
  $("timeline").innerHTML = '<svg viewBox="0 0 ' + w + " " + h + '" role="img" aria-label="' + M.chart + '" font-size="11" fill="currentColor">'
    + '<rect x="0.5" y="0.5" width="' + (w - 1) + '" height="' + (h - 1) + '" fill="none" stroke="currentColor" opacity="0.4"/>'
    + grid + body + legend
    + '<text x="' + left + '" y="' + (h - 5) + '">0 ' + M.secs + "</text>"
    + '<text x="' + (w - right) + '" y="' + (h - 5) + '" text-anchor="end">' + (steps * stepSeconds).toFixed(1) + " " + M.secs + "</text>"
    + '<text x="' + (w - right) + '" y="13" text-anchor="end">LUFS</text></svg>';
};

const measure = async () => {
  say(M.measuring);
  report = (await call("Measure")).result;
  timeline = (await call("GetTimeline")).result;
  showResults();
  drawTimeline();
};

const normalize = latest(async () => {
  const target = Number($("target").value);
  const integrated = report[0];
  const notes = [];
  if (!Number.isFinite(integrated)) {
    $("normalized").hidden = true;
    $("gainNote").textContent = M.silent;
    $("measuredNote").textContent = "";
    $("warning").textContent = "";
    return;
  }
  const gain = target - integrated;
  const highest = Math.max(...Array.from({ length: info.channels }, (_, c) => db(report[8 + info.channels + c])));
  const rendered = (await call("Render", gain)).result;
  const [again, , truePeak, clipped] = (await call("GetRenderReport")).result;
  setWave("normalized", rendered);
  $("gainNote").textContent = fill(M.gain, { gain: signed(gain), peak: fmt(highest + gain) });
  $("measuredNote").textContent = fill(M.measuredAgain, { loudness: fmt(again), peak: fmt(db(truePeak)) });
  if (highest + gain > PEAK_LIMIT) notes.push(fill(M.peakWarning, { limit: PEAK_LIMIT }));
  if (clipped > 0) notes.push(fill(M.clipped, { n: clipped }));
  $("warning").textContent = notes.join(" ");
});

const loaded = async (loadedInfo, original, truncated) => {
  info = { channels: loadedInfo[0], rate: loadedInfo[1], frames: loadedInfo[2], bits: loadedInfo[3], original: loadedInfo[4] };
  showInfo(truncated);
  if (original) {
    setSource("original", original);
  } else {
    setWave("original", (await call("RenderOriginal")).result);
  }
  await measure();
  await normalize();
  $("timing").hidden = true;
  $("allocation").textContent = "";
  say(M.ready);
};

const guard = (task) => async () => {
  enable(false);
  try {
    await task();
  } catch (error) {
    fail(error);
  } finally {
    enable(info !== null);
  }
};

const isWave = (bytes) => bytes.length >= 12
  && String.fromCharCode(...bytes.subarray(0, 4)) === "RIFF"
  && String.fromCharCode(...bytes.subarray(8, 12)) === "WAVE";

const decode = async (bytes) => {
  let context;
  try { context = new AudioContext({ sampleRate: DECODE_RATE }); } catch { context = new AudioContext(); }
  try {
    const buffer = await context.decodeAudioData(bytes.buffer.slice(0));
    const frames = Math.min(buffer.length, Math.floor(buffer.sampleRate * MAX_SECONDS));
    const channels = buffer.numberOfChannels;
    const interleaved = new Float32Array(frames * channels);
    for (let c = 0; c < channels; c++) {
      const data = buffer.getChannelData(c);
      for (let i = 0; i < frames; i++) interleaved[i * channels + c] = data[i];
    }
    return { interleaved, channels, rate: buffer.sampleRate, truncated: buffer.length > frames };
  } finally {
    context.close();
  }
};

const loadSource = guard(async () => {
  const kind = Number($("source").value);
  $("sampleNote").hidden = kind !== 3;
  $("file").value = "";
  say(M.loadingAudio);
  if (kind === 3) {
    const url = $("sampleNote").dataset.src;
    const bytes = new Uint8Array(await (await fetch(url)).arrayBuffer());
    await loaded((await call("LoadWave", bytes)).result, url, false);
  } else {
    await loaded((await call("Generate", kind)).result, null, false);
  }
});

const loadFile = guard(async () => {
  const file = $("file").files[0];
  if (!file) return;
  $("sampleNote").hidden = true;
  say(M.decoding);
  const bytes = new Uint8Array(await file.arrayBuffer());
  let result = null;
  if (isWave(bytes)) {
    try { result = (await call("LoadWave", bytes)).result; } catch { result = null; }
  }
  if (result) {
    await loaded(result, file, result[4] > result[2]);
    return;
  }
  const decoded = await decode(bytes);
  const samples = new Uint8Array(decoded.interleaved.buffer);
  result = (await call("LoadSamples", samples, decoded.channels, decoded.rate)).result;
  await loaded(result, file, decoded.truncated || result[4] > result[2]);
});

const time = latest(async () => {
  $("time").disabled = true;
  say(M.running);
  const times = (await call("TimeModes")).result;
  const allocation = (await call("CheckAllocation")).result;
  const seconds = info.frames / info.rate;
  for (let i = 0; i < MODE_COUNT; i++) {
    $("time" + i).textContent = fmt(times[i], times[i] < 10 ? 2 : 1) + " " + M.ms;
    $("speed" + i).textContent = "×" + Math.round(seconds / (times[i] / 1000)).toLocaleString("en-US");
  }
  $("timing").hidden = false;
  const [calibration, measured] = allocation;
  $("allocation").textContent = calibration >= 1000000
    ? fill(M.allocation, { bytes: measured, calibration })
    : M.allocationUnavailable;
  $("time").disabled = false;
  say(M.ready);
});

const start = () => {
  loadSource().catch(fail);
};

const bindTarget = () => {
  const target = $("target");
  const show = () => { $("targetOut").textContent = fmt(Number(target.value)) + " LUFS"; };
  show();
  target.addEventListener("input", () => {
    show();
    const match = Array.from($("preset").options).find((option) => Number(option.value) === Number(target.value));
    $("preset").value = match ? match.value : "custom";
    if (report) normalize();
  });
  $("preset").addEventListener("change", () => {
    if ($("preset").value === "custom") return;
    target.value = $("preset").value;
    show();
    if (report) normalize();
  });
};

bindTarget();
$("source").addEventListener("change", loadSource);
$("file").addEventListener("change", loadFile);
$("time").addEventListener("click", time);

say(fill(M.loading, { loaded: "0.0" }));

let demo;
let loaded = 0;
let total = 0;

const inflate = async (uri) => {
  const response = await fetch(uri + ".gz");
  if (!response.ok) throw new Error(uri + " " + response.status);
  total += Number(response.headers.get("content-length")) || 0;
  const counted = response.body.pipeThrough(new TransformStream({
    transform(chunk, controller) {
      loaded += chunk.length;
      postMessage({ progress: [loaded, total] });
      controller.enqueue(chunk);
    },
  }));
  return new Response(counted.pipeThrough(new DecompressionStream("gzip")), { headers: { "Content-Type": "application/wasm" } });
};

const ready = (async () => {
  const { dotnet } = await import("./_framework/dotnet.js");
  const runtime = await dotnet
    .withResourceLoader((type, name, uri) => (type === "assembly" || type === "dotnetwasm") ? inflate(uri) : null)
    .create();
  demo = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)).Demo;
  postMessage({ ready: true });
})();

ready.catch((error) => postMessage({ failed: String((error && error.message) || error) }));

addEventListener("message", async (event) => {
  const { id, name, args } = event.data;
  try {
    await ready;
    let result = demo[name](...args);
    const milliseconds = demo.GetMilliseconds();
    if (result instanceof Uint8Array) result = result.slice();
    postMessage({ id, result, milliseconds }, result instanceof Uint8Array ? [result.buffer] : []);
  } catch (error) {
    postMessage({ id, error: String((error && error.message) || error) });
  }
});

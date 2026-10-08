#!/bin/sh
set -eu
cd "$(dirname "$0")"
rm -rf out obj
dotnet publish -c Release -o out
framework=out/wwwroot/_framework
target=../docs/demo/_framework
rm -rf "$target"
mkdir -p "$target"
cp "$framework"/dotnet.js "$framework"/dotnet.native.*.js "$framework"/dotnet.runtime.*.js "$target"/
for file in "$framework"/*.wasm.gz; do
  gzip -dc "$file" | gzip -9n > "$target/$(basename "$file")"
done

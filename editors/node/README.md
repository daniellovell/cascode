# @cascode/cascode-js

Synchronous Node binding for the Cascode native C ABI (`libcascode`).

This package exposes a thin N-API wrapper around the `cascode_*` exports from
`Cascode.Native` and intentionally does not add threading or async behavior.

## Install

```bash
npm install @cascode/cascode-js
```

## Runtime Requirements

`@cascode/cascode-js` needs two native layers:

1. Node addon (`cascode_native_addon.node`)
2. Cascode shared library (`libcascode.*`)

At runtime the loader resolves in this order:

1. Platform package (installed as an optional dependency):
   - `@cascode/cascode-js-darwin-arm64`
   - `@cascode/cascode-js-darwin-x64`
   - `@cascode/cascode-js-linux-x64`
   - `@cascode/cascode-js-win32-x64`
2. Local build (`editors/node/build/Release`)
3. Local native runtime discovery:
   - `editors/node/native/<rid>/`
   - `<repo>/build/native/<rid>/`

`CASCODE_NATIVE_LIB` always overrides discovered shared-library paths.

RID examples: `win-x64`, `linux-x64`, `darwin-x64`, `darwin-arm64`.

## API

The package exports low-level session management and convenience wrappers for each native method.

```js
const cascode = require("@cascode/cascode-js");

const session = cascode.createSession();
const doc = cascode.open(cascode.native, session, { path: "my_circuit.cas" });
const schematic = cascode.render(cascode.native, session, { documentId: doc.documentId });
cascode.destroySession(session);
```

Session lifecycle: `createSession`, `destroySession`, `call`, `lastErrorJson`, `apiVersion`, `schemaVersion`.

Document operations: `open`, `updateText`, `close`.

Schematic: `render`, `applyOps`.

Analysis: `erc`, `emit`, `verify`.

Jobs (long-running): `jobStart`, `jobPoll`, `jobCancel`.

`stdlibPath` provides the absolute path to the bundled standard library.

## Build

```bash
cd editors/node
npm ci --omit=optional
npm run build
```

## Test

Publish a native library first, then run tests:

```bash
dotnet publish tools/native/Cascode.Native/Cascode.Native.csproj \
  --configuration Release \
  -r linux-x64 \
  -p:PublishAot=true \
  -o build/native/linux-x64

export CASCODE_NATIVE_LIB="$PWD/build/native/linux-x64/Cascode.Native.so"
cd editors/node
npm test
```

Windows equivalent:

```powershell
dotnet publish tools/native/Cascode.Native/Cascode.Native.csproj `
  --configuration Release `
  -r win-x64 `
  -p:PublishAot=true `
  -o build/native/win-x64

$env:CASCODE_NATIVE_LIB="$PWD\build\native\win-x64\Cascode.Native.dll"
cd editors\node
npm test
```

# @cascode/native

Synchronous Node binding for the Cascode native C ABI.

This package exposes a thin N-API wrapper around the `cascode_*` exports from
`Cascode.Native` and intentionally does not add threading or async behavior.

## Install

```bash
npm install @cascode/native
```

## Runtime Requirements

`@cascode/native` needs two native layers:

1. Node addon (`cascode_native_addon.node`)
2. Cascode shared library (`Cascode.Native.*`) plus runtime dependencies

At runtime, `@cascode/native` resolves in this order:

1. Platform package (optional dependency), for example:
   - `@cascode/native-win32-x64`
   - `@cascode/native-linux-x64`
   - `@cascode/native-darwin-x64`
   - `@cascode/native-darwin-arm64`
2. Local development build (`editors/node/build/Release`)
3. Local native runtime discovery:
  - `editors/node/native/<rid>/`
  - `<repo>/build/native/<rid>/`

`CASCODE_NATIVE_LIB` always overrides discovered shared-library paths.

RID examples: `win-x64`, `linux-x64`, `darwin-x64`, `darwin-arm64`.

## Local Co-Development With Designer

For active development across repos, point Designer at your local module path
instead of waiting for a release:

```powershell
$env:DESIGNER_CASCODE_NATIVE_MODULE="C:\Projects\Repositories\cascode\editors\node"
```

Then in `cascode/editors/node`:

```powershell
npm ci --omit=optional
npm run build
```

And publish local native runtime once:

```powershell
dotnet publish tools/native/Cascode.Native/Cascode.Native.csproj `
  --configuration Release `
  -r win-x64 `
  -p:PublishAot=true `
  -o build/native/win-x64
```

## Build

```bash
npm ci
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

$env:CASCODE_NATIVE_LIB="$PWD\\build\\native\\win-x64\\Cascode.Native.dll"
cd editors\node
npm test
```

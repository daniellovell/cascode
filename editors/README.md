# Editor Support

This directory contains editor-facing Cascode integrations and syntax support.

## Available integrations

- VS Code / Cursor / VSCodium extension: [editors/vscode](./vscode)
- Node native runtime package: [editors/node](./node)
- GitHub Linguist config: [editors/linguist/cascode.yml](./linguist/cascode.yml)

## VS Code syntax support

The VS Code extension provides TextMate syntax highlighting, language configuration, and install
scripts for local development. See [editors/vscode/README.md](./vscode/README.md).

The grammar is intended to track the current language surface described in
[spec/language](../spec/language/README.md), including declarations such as `bundle`, `interface`,
`circuit`, `bench`, and `primitive`, plus current connectivity syntax like `--`, `attach`,
`constraints`, `harness`, and directional terminals (`input`, `output`, `io`).

## Native editor API

The Node package in [editors/node](./node) exposes the native runtime for editor integrations. See
[editors/node/README.md](./node/README.md) for build, test, and packaging details.

## GitHub Linguist

The repository includes [editors/linguist/cascode.yml](./linguist/cascode.yml) plus the repo root
`.gitattributes` configuration so GitHub can classify `.cas` files as Cascode. Full GitHub syntax
highlighting still requires the grammar to be upstreamed to `github/linguist`.

## Contributing

When updating syntax support:

1. Keep the grammar aligned with the current language spec, not historical syntax.
2. Test against real `.cas` fixtures from the repo.
3. Update the relevant editor README if installation or capability changes.

# Cascode Language Support for VS Code

Provides syntax highlighting and language support for Cascode (`.cas`) files.

## Features

- **Syntax Highlighting**: Full syntax highlighting for Cascode language constructs
  - Keywords: `class`, `motif`, `package`, `import`, `slot`, `synth`, `spec`, `bench`, etc.
  - Typed units: `1.8V`, `15pF`, `50Ω`, `27C`, `1.2ns`, `50MHz`, `60deg`, `70dB`
  - Connection operators: `->`, `<-`, `<->`
  - Built-in types: `electrical`, `supply`, `ground`, `port`, `net`
  - Comments: line (`//`) and block (`/* */`)
  - Spec functions: `rise_time`, `fall_time`, `gbw`, `pm`, `gain`, `swing`, etc.
  - Common motifs and traits

- **Smart Editing**:
  - Auto-closing brackets, braces, and quotes
  - Comment toggling with `Ctrl+/` (or `Cmd+/` on macOS)
  - Bracket matching
  - Smart indentation

## Installation

### Option 1: Install from VSIX (Local Installation)

1. Package the extension:
   ```bash
   cd editors/vscode
   npm install -g @vscode/vsce
   vsce package
   ```

2. Install the generated `.vsix` file in VS Code:
   - Open VS Code
   - Press `Ctrl+Shift+P` (or `Cmd+Shift+P` on macOS)
   - Type "Extensions: Install from VSIX..."
   - Select the generated `cascode-lang-0.1.0.vsix` file

### Option 2: Development Mode

1. Copy or symlink this directory to your VS Code extensions folder:
   - **Windows**: `%USERPROFILE%\.vscode\extensions\cascode-lang`
   - **macOS/Linux**: `~/.vscode/extensions/cascode-lang`

2. Restart VS Code

3. Open a `.cas` file to see syntax highlighting

### Option 3: Publish to Marketplace (Future)

For wider distribution, publish to the VS Code Marketplace:

```bash
cd editors/vscode
vsce publish
```

## Requirements

None - this is a pure syntax highlighting extension.

## Extension Settings

This extension doesn't add any VS Code settings currently.

## Known Issues

None yet. Please report issues on the [GitHub repository](https://github.com/cascode/cascode/issues).

## Contributing

Contributions are welcome! See the main repository's CONTRIBUTING.md for guidelines.

## Release Notes

### 0.1.0

- Initial release
- Syntax highlighting for all Cascode language constructs
- Support for typed units (V, A, F, Ω, Hz, s, W, C, K, dB, deg, %)
- Connection operators and range syntax
- Auto-closing pairs and smart indentation

## License

BSD-3 (matches main cascode repository)



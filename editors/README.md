# Cascode Syntax Highlighting

This directory contains syntax highlighting support for the Cascode language (`.cas` files) across various editors and platforms.

## 🎨 What's Included

### VS Code / VS Codium / Cursor

- **Location**: `vscode/`
- **Installation**: See [vscode/README.md](vscode/README.md)
- **Features**: Full syntax highlighting, auto-closing pairs, smart indentation

### GitHub & GitHub Linguist

- **Configuration**: `.gitattributes` in repo root + `linguist/cascode.yml`
- **Status**: The `.gitattributes` file marks `.cas` files for Linguist, but **full GitHub highlighting requires the grammar to be added to the [github/linguist](https://github.com/github/linguist) repository**
- **See**: [How to Add GitHub Support](#github-support) below

### Other Editors

- **Sublime Text, TextMate, Atom**: Can use the TextMate grammar at `vscode/syntaxes/cascode.tmLanguage.json`
- **Vim/Neovim**: See [vim/README.md](vim/README.md) (if created)
- **Emacs**: See [emacs/README.md](emacs/README.md) (if created)

## 🚀 Quick Start

### For VS Code Users

1. Install the extension locally:
   ```bash
   cd editors/vscode
   # If you have vsce installed:
   vsce package
   # Then install the .vsix in VS Code: Extensions > ... > Install from VSIX
   
   # OR symlink for development:
   # Windows
   mklink /D "%USERPROFILE%\.vscode\extensions\cascode-lang" "%CD%"
   # macOS/Linux  
   ln -s "$(pwd)" ~/.vscode/extensions/cascode-lang
   ```

2. Restart VS Code and open a `.cas` file

### For GitHub Users

The `.gitattributes` file is already configured. However, for **full syntax highlighting on GitHub.com**, you need to:

1. Fork [github/linguist](https://github.com/github/linguist)
2. Add `editors/linguist/cascode.yml` to `lib/linguist/languages.yml`
3. Copy `vscode/syntaxes/cascode.tmLanguage.json` to `grammars/cascode.tmLanguage.json` in the linguist repo
4. Submit a pull request to linguist

Until then, GitHub will recognize `.cas` files as "Cascode" language (via `.gitattributes`) but won't apply syntax highlighting.

## 📝 Language Features Highlighted

The TextMate grammar recognizes:

### Keywords

- **Package/Import**: `package`, `import`
- **Declarations**: `module`, `motif`, `trait`, `extend`, `implements`
- **Blocks**: `supply`, `ground`, `port`, `net`, `param`, `env`, `use`, `spec`, `bench`, `synth`, `slot`, `phase`
- **Directives**: `from`, `allow`, `prefer`, `forbid`, `objective`, `minimize`, `maximize`, `fill`, `bind`, `with`, `wrap`, `spice`, `map`
- **Structure**: `attach`, `fb`, `pair`, `new`
- **Port Roles**: `in`, `out`, `diff`, `clock`, `bias`

### Typed Units

- **Voltage**: `1.8V`, `0.9*VDD`, `mV`, `µV`
- **Current**: `1mA`, `500µA`, `nA`
- **Capacitance**: `15pF`, `2pF`, `nF`
- **Time**: `1.2ns`, `300ps`, `1us`
- **Frequency**: `50MHz`, `100MHz`, `GHz`
- **Temperature**: `27C`, `125C`, `K`
- **Power**: `2mW`, `500uW`
- **Angle**: `60deg`, `45deg`
- **Decibels**: `70dB`, `-3dB`
- **Percentage**: `10%`, `50%`

### Operators

- **Connection**: `->` (bind/connect; preferred), `<->` (bidirectional). `<-` is still recognized by the language, but the house style uses `->`.
- **Range**: `..` (e.g., `[0.5V..0.8V]`)
- **Comparison**: `==`, `!=`, `<=`, `>=`, `<`, `>`
- **Arithmetic**: `+`, `-`, `*`, `/`, `%`

### Built-in Functions & Benches

- **Spec functions**: `GainBandwidth`, `PassbandGain`, `PhaseMargin`, `OutputSwing`, `NoiseIn`, `SlewRate`, `Settle`, `ZeroTau`, `Power`, `DynamicPower`, `TogglePower`, `Headroom`, `ICMR`, `RiseTime`, `FallTime`, `VOH`, `VOL`, `Area`
- **Bench names**: `SEOpAmpACBench`, `SEAmpACBench`, `FDOpAmpACBench`, `UnityUGF`, `Step`, `NoiseIn`, `StepToggle`
- **Components**: `C(...)`, `R(...)`

### Types & Traits

- **Port types**: `signal`, `analog`, `digital`, `mixed`, `supply`, `ground`, `bias`, `rf`, `clock`
- **Primitives**: `int`, `float`, `double`, `bool`, `string`
- **Common traits**: `Amplifier`, `SingleEndedOpAmp`, `SingleEndedAmp`, `FullyDiffOpAmp`, `Comparator`, `CurrentMirror`, `InverterLike`
- **Common motifs**: `DiffPair`, `CascodePair`, `CurrentMirror`, `StrongArmLatch`, `MillerRC`, `PadDriver`

## 🔧 Extending the Grammar

To add new keywords or patterns:

1. Edit `vscode/syntaxes/cascode.tmLanguage.json`
2. Add patterns to the appropriate repository section
3. Test with VS Code (reload window: `Ctrl+Shift+P` → "Reload Window")
4. Update this README with new features

### Pattern Categories

- `comments`: Line (`//`) and block (`/* */`) comments
- `keywords`: Language keywords and declarations
- `strings`: Double, single, and triple-quoted strings
- `numbers`: Integers, floats, scientific notation, hex
- `units`: All physical units with SI prefixes
- `operators`: Comparison, arithmetic, logical, connection, range
- `functions`: Spec functions, bench names, built-ins
- `types`: Traits, motifs, and user-defined types
- `constants`: Booleans, null, special names (VDD, GND, etc.)

## 🌐 GitHub Support

### Current Status

- ✅ `.gitattributes` configured to mark `.cas` as Cascode
- ⚠️ GitHub won't highlight syntax until Linguist is updated
- 📋 Manual highlighting works in markdown with ` ```cas ` code fences

### Adding Official GitHub Support

To get syntax highlighting on GitHub.com, the Cascode grammar needs to be added to [github/linguist](https://github.com/github/linguist):

1. **Fork linguist**: https://github.com/github/linguist

2. **Add language definition**:
   Edit `lib/linguist/languages.yml` and add:
   ```yaml
   Cascode:
     type: programming
     color: "#6A5ACD"
     extensions:
     - ".cas"
     tm_scope: source.cascode
     ace_mode: text
     language_id: 987654321
   ```

3. **Add grammar**:
   Copy `vscode/syntaxes/cascode.tmLanguage.json` to `grammars/source.cascode.json` in linguist

4. **Add sample**:
   Add a representative `.cas` file to `samples/Cascode/` (e.g., one of the examples)

5. **Run tests**:
   ```bash
   bundle install
   bundle exec rake samples
   bundle exec rake test
   ```

6. **Submit PR** to linguist with:
   - Clear description of Cascode language
   - Link to this repository
   - Link to language spec (if published)

### Temporary Solution for GitHub

Until linguist is updated, use markdown code fences with manual syntax specification in README files:

````markdown
```cascode
module AmpAuto implements SingleEndedOpAmp {
  supply VDD = 1.2V; ground GND;
  spec { GainBandwidth>=100MHz; PhaseMargin>=60deg; }
}
```
````

Or use HTML with manual styling (not recommended, hard to maintain).

## 📚 Additional Resources

- [TextMate Language Grammar](https://macromates.com/manual/en/language_grammars)
- [VS Code Syntax Highlighting Guide](https://code.visualstudio.com/api/language-extensions/syntax-highlight-guide)
- [GitHub Linguist Documentation](https://github.com/github/linguist/blob/master/CONTRIBUTING.md)
- [Cascode Language Specification](../../spec/language/)

## 🤝 Contributing

Improvements to syntax highlighting are welcome! Please:

1. Test changes with real `.cas` files from `examples/`
2. Ensure the grammar works in VS Code
3. Update this README with new features
4. Submit a PR with example screenshots

## 📄 License

BSD-3 (matches main cascode repository)




# Cascode Language Support for VS Code / Cursor

Provides syntax highlighting for Cascode (`.cas`, `.cai`) files.

## Install (dev mode)

```bash
cd editors/vscode
chmod +x install.sh
./install.sh
```

On Windows PowerShell, bypass the execution policy for the current session and run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File install.ps1
```

If the extension doesn't load, reload the editor window.

## Notes

- The installer copies extension files to your extensions folder.
- Existing installations are automatically backed up with a timestamp.
- Re-run the installer anytime to update the extension after grammar changes.



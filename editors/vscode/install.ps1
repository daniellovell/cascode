$ErrorActionPreference = 'Stop'
Write-Host "🎨 Installing Cascode syntax highlighting..."

$SrcDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Targets = New-Object System.Collections.Generic.List[string]

function Add-Target {
  param([string] $Path)

  if ([string]::IsNullOrWhiteSpace($Path)) {
    return
  }

  if (-not $Targets.Contains($Path)) {
    $Targets.Add($Path)
  }
}

if ($env:USERPROFILE) {
  Add-Target (Join-Path $env:USERPROFILE ".vscode\extensions\cascode-lang")
  Add-Target (Join-Path $env:USERPROFILE ".cursor-server\extensions\cascode-lang")
  Add-Target (Join-Path $env:USERPROFILE ".cursor\extensions\cascode-lang")
}

$CursorAppData = [Environment]::GetFolderPath('ApplicationData')
if (-not [string]::IsNullOrWhiteSpace($CursorAppData)) {
  Add-Target (Join-Path $CursorAppData "Cursor\User\extensions\cascode-lang")
}

if ($Targets.Count -eq 0) {
  Write-Error "No installation targets detected. Verify USERPROFILE and APPDATA are set."
  exit 1
}

foreach ($Target in $Targets) {
  $Parent = Split-Path -Parent $Target
  New-Item -ItemType Directory -Force -Path $Parent | Out-Null
  
  if (Test-Path $Target) {
    $Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $Backup = "${Target}.backup.${Timestamp}"
    Write-Host "  Backing up existing installation to $(Split-Path -Leaf $Backup)"
    Move-Item $Target $Backup
  }
  
  Write-Host "  Copying to $(Split-Path -Leaf $Parent) ($Target)"
  Copy-Item -Recurse -Force $SrcDir $Target
  Write-Host "  ✅ Installed to $(Split-Path -Leaf $Parent)"
}

Write-Host ""
Write-Host "✨ Installation complete!"
Write-Host ""
Write-Host "📝 Next steps:"
Write-Host "   1. Restart your editor"
Write-Host "   2. Open any .cas file"
Write-Host "   3. Syntax highlighting should work automatically"



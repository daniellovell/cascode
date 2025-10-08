$ErrorActionPreference = 'Stop'
Write-Host "🎨 Installing Cascode syntax highlighting..."

$SrcDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Targets = @(
  Join-Path $env:USERPROFILE ".vscode\extensions\cascode-lang",
  Join-Path $env:USERPROFILE ".cursor-server\extensions\cascode-lang"
)

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



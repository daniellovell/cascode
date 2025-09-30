# Install Cascode syntax highlighting for VS Code (Windows)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$vsCodeExtDir = "$env:USERPROFILE\.vscode\extensions"
$cursorExtDir = "$env:USERPROFILE\.cursor\extensions"
$vscodiumExtDir = "$env:USERPROFILE\.vscode-oss\extensions"

Write-Host "🎨 Installing Cascode syntax highlighting..." -ForegroundColor Cyan

function Test-EditorExists {
    param([string]$editorCmd)
    
    $null -ne (Get-Command $editorCmd -ErrorAction SilentlyContinue)
}

function Install-ToDir {
    param(
        [string]$extDir,
        [string]$editorName,
        [string]$editorCmd
    )
    
    # Check if editor executable exists
    if (-not (Test-EditorExists $editorCmd)) {
        Write-Host "  ⏭️  $editorName not found (no '$editorCmd' command), skipping" -ForegroundColor Gray
        return $false
    }
    
    # Create extensions directory if it doesn't exist
    if (-not (Test-Path $extDir)) {
        Write-Host "  Creating extensions directory: $extDir" -ForegroundColor Yellow
        New-Item -ItemType Directory -Path $extDir -Force | Out-Null
    }
    
    $target = Join-Path $extDir "cascode-lang"
    
    # Remove existing if present
    if (Test-Path $target) {
        Write-Host "  Removing existing installation at $target" -ForegroundColor Yellow
        Remove-Item -Path $target -Recurse -Force
    }
    
    # Create junction (Windows symlink equivalent)
    Write-Host "  Installing to $editorName ($target)" -ForegroundColor Green
    cmd /c mklink /J "$target" "$scriptDir" | Out-Null
    Write-Host "  ✅ Installed to $editorName" -ForegroundColor Green
    return $true
}

# Try installing to various editors
$installedCount = 0

if (Install-ToDir $vsCodeExtDir "VS Code" "code") {
    $installedCount++
}

if (Install-ToDir $cursorExtDir "Cursor" "cursor") {
    $installedCount++
}

if (Install-ToDir $vscodiumExtDir "VSCodium" "codium") {
    $installedCount++
}

if ($installedCount -eq 0) {
    Write-Host ""
    Write-Host "❌ No compatible editors found!" -ForegroundColor Red
    Write-Host "   Please install VS Code, Cursor, or VSCodium first." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✨ Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "📝 Next steps:" -ForegroundColor Cyan
Write-Host "   1. Restart your editor"
Write-Host "   2. Open any .cas file"
Write-Host "   3. Syntax highlighting should work automatically"
Write-Host ""
Write-Host "   If colors don't appear, try:" -ForegroundColor Yellow
Write-Host "   - Press Ctrl+Shift+P"
Write-Host "   - Type 'Developer: Reload Window'"
Write-Host ""
exit 0

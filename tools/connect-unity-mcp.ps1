# Connect MCP for Unity to Cursor and Claude Code.
# Requires: Unity 6 project already has com.coplaydev.unity-mcp.

$ErrorActionPreference = "Stop"
$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe"
$ProjectPath = "C:\lab\HRI-Robot"
$McpUrl = "http://127.0.0.1:8080/mcp"
$HttpBase = "http://127.0.0.1:8080"

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

function Test-Command($name) {
    return [bool](Get-Command $name -ErrorAction SilentlyContinue)
}

Write-Step "Check uv / uvx"
if (-not (Test-Command "uvx")) {
    Write-Host "uv is not on PATH. Installing via official installer..."
    powershell -NoProfile -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
    $env:Path = "$env:USERPROFILE\.local\bin;$env:USERPROFILE\AppData\Local\uv;$env:Path"
    if (Test-Path "$env:USERPROFILE\.local\bin") {
        $env:Path = "$env:USERPROFILE\.local\bin;$env:Path"
    }
    if (-not (Test-Command "uvx")) {
        throw "uvx still not found after install. Close this terminal, reopen, and re-run this script."
    }
}
uv --version
uvx --version

Write-Step "Register UnityMCP with Claude Code (HTTP :8080)"
$claude = Get-Command claude -ErrorAction SilentlyContinue
if ($claude) {
    Push-Location $ProjectPath
    try {
        claude mcp remove --scope local UnityMCP 2>$null
        claude mcp remove --scope user UnityMCP 2>$null
        claude mcp remove --scope project UnityMCP 2>$null
        claude mcp add --scope local --transport http UnityMCP $McpUrl
        claude mcp list
    } finally {
        Pop-Location
    }
} else {
    Write-Host "claude CLI not found on PATH. Skipping Claude Code registration."
}

Write-Step "Start Unity Editor if needed"
$unityRunning = Get-Process -Name "Unity" -ErrorAction SilentlyContinue
if (-not $unityRunning) {
    if (-not (Test-Path $UnityExe)) {
        throw "Unity.exe not found: $UnityExe"
    }
    Start-Process -FilePath $UnityExe -ArgumentList @("-projectPath", $ProjectPath)
    Write-Host "Unity Editor launching. Wait until the project is fully open."
} else {
    Write-Host "Unity already running (PID $($unityRunning.Id -join ', '))."
}

Write-Step "Start MCP for Unity HTTP server on :8080 if needed"
try {
    $conn = Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction Stop
    Write-Host "Port 8080 already listening (PID $($conn.OwningProcess))."
} catch {
    Write-Host "Starting: uvx --prerelease explicit --from mcpforunityserver>=0.0.0a0 mcp-for-unity --transport http --http-url $HttpBase --project-scoped-tools"
    Start-Process -FilePath "uvx" -ArgumentList @(
        "--prerelease", "explicit",
        "--from", "mcpforunityserver>=0.0.0a0",
        "mcp-for-unity",
        "--transport", "http",
        "--http-url", $HttpBase,
        "--project-scoped-tools"
    ) -WindowStyle Minimized
    Start-Sleep -Seconds 3
}

Write-Host "`nDone. Next in Unity:" -ForegroundColor Green
Write-Host "  1. Window > MCP for Unity"
Write-Host "  2. Start Bridge if it shows Stopped"
Write-Host "  3. If Pending Connection appears, click Accept"
Write-Host "  4. Cursor Settings > MCP で unityMCP が緑色になることを確認"
Write-Host "  5. Claude Code を開き直す"

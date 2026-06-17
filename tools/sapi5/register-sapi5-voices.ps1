<#
.SYNOPSIS
    Register the http_sapi5_engine.dll as a SAPI5 TTS provider and create
    voice tokens that surface in Zotero's "Local/Chosen Application" tier.

.DESCRIPTION
    The engine DLL is a thin SAPI5 wrapper that POSTs to a local
    OpenAI-compatible TTS server (the one shipped in tools/openai_edge_tts_server.py
    or a MiniMax proxy). One Windows registry voice token is created per
    entry in the $voices array; each token reads Endpoint / Path / VoiceId
    from its Attributes subkey at SAPI5 enumeration time, so adding a new
    voice is a one-line edit + re-run.

.PARAMETER DllPath
    Absolute path to the compiled engine. Default:
        C:\Program Files\MinimaxTTS\http_sapi5_engine.dll

.PARAMETER Unregister
    Tear everything down: unregisters the COM class and removes every
    voice token whose CLSID matches this engine.

.EXAMPLE
    PS> .\register-sapi5-voices.ps1
    Registers the DLL and writes voice tokens from $voices below.

.EXAMPLE
    PS> .\register-sapi5-voices.ps1 -DllPath C:\Path\To\http_sapi5_engine.dll
    Uses a custom DLL location.

.EXAMPLE
    PS> .\register-sapi5-voices.ps1 -Unregister
    Reverses the install.
#>
[CmdletBinding()]
param(
    [string] $DllPath = "C:\Program Files\MinimaxTTS\http_sapi5_engine.dll",
    [ValidateSet("HKCU", "HKLM")]
    [string] $Hive = "HKCU",
    [string] $VoicesJson = "",
    [switch] $Unregister
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

# Must match the CLSID in http_sapi5_engine.cpp.
$Clsid = "{72A7DE4B-327C-4EB8-A402-A280F4F41A05}"
$Vendor = "MinimaxTTS"
$Sapi5Root = "${Hive}:\SOFTWARE\Microsoft\Speech\Voices\Tokens"

# Edit this list to match the MiniMax / OpenAI-compatible voices you want
# surfaced in Zotero. Field semantics:
#   Token    registry token name (must be unique on the system)
#   Name     display name shown in the Zotero voice dropdown
#   Lang     hex LCID, e.g. 0804 = zh-CN, 0409 = en-US, 0411 = ja-JP
#   Gender   "Male" / "Female" / "Neutral"
#   Provider "edge" / "minimax" / another provider accepted by your local server
#   Model    provider model, e.g. speech-2.8-hd for MiniMax
#   Voice    provider voice id, sent verbatim as the "voice" field in JSON
#   Endpoint local server base URL (no trailing slash)
#   Path     local server path, defaults to /v1/speech
#   Rate     provider speed multiplier, 1.0 = normal
#   TimeoutMs per-request network timeout for the SAPI5 bridge
$voices = @(
    @{
        Token    = "MinimaxTTS-zh-Yunxi"
        Name     = "MinimaxTTS - Chinese Yunxi"
        Lang     = "0804"
        Gender   = "Female"
        Provider = "edge"
        Model    = "tts-1"
        Voice    = "zh-CN-YunxiNeural"
        Endpoint = "http://127.0.0.1:5050"
        Path     = "/v1/speech"
        Rate     = "1.0"
        TimeoutMs = "15000"
    },
    @{
        Token    = "MinimaxTTS-en-Aria"
        Name     = "MinimaxTTS - English Aria"
        Lang     = "0409"
        Gender   = "Female"
        Provider = "edge"
        Model    = "tts-1"
        Voice    = "en-US-AriaNeural"
        Endpoint = "http://127.0.0.1:5050"
        Path     = "/v1/speech"
        Rate     = "1.0"
        TimeoutMs = "15000"
    }
    # Add more entries here. Voice must exist in the local server's accept
    # list, such as Edge voice ids or MiniMax proxy voice ids.
)

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Assert-Administrator {
    # Per-user registration (HKCU, the default) does not require elevation.
    # For HKLM registration, re-run PowerShell as Administrator.
    if ($Hive -eq "HKLM") {
        $id = [Security.Principal.WindowsIdentity]::GetCurrent()
        $pr = New-Object Security.Principal.WindowsPrincipal($id)
        if (-not $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw "HKLM registration requires Administrator. Re-launch PowerShell elevated and pass -Hive HKLM."
        }
    }
}

function Test-LocalServerReachable {
    param([string] $Endpoint, [string] $Path)
    $client = $null
    try {
        $uri = [Uri]::new($Endpoint)
        $port = $uri.Port
        if ($uri.IsDefaultPort) {
            $port = if ($uri.Scheme -eq "https") { 443 } else { 80 }
        }
        $client = New-Object Net.Sockets.TcpClient
        $iar = $client.BeginConnect($uri.Host, $port, $null, $null)
        if (-not $iar.AsyncWaitHandle.WaitOne(4000, $false)) {
            $client.Close()
            return $false
        }
        $client.EndConnect($iar)
        $client.Close()
        return $true
    } catch {
        if ($client) { $client.Close() }
        return $false
    }
}

function ConvertTo-Hashtable {
    param([Parameter(ValueFromPipeline=$true)] $InputObject)
    process {
        if ($null -eq $InputObject) { return $null }
        if ($InputObject -is [hashtable]) { return $InputObject }
        if ($InputObject -is [pscustomobject]) {
            $h = @{}
            foreach ($prop in $InputObject.PSObject.Properties) { $h[$prop.Name] = ConvertTo-Hashtable $prop.Value }
            return $h
        }
        if ($InputObject -is [System.Collections.IDictionary]) {
            $h = @{}
            foreach ($key in $InputObject.Keys) { $h[$key] = ConvertTo-Hashtable $InputObject[$key] }
            return $h
        }
        if ($InputObject -is [System.Collections.IEnumerable] -and $InputObject -isnot [string]) {
            $arr = @()
            foreach ($item in $InputObject) { $arr += ,(ConvertTo-Hashtable $item) }
            return $arr
        }
        return $InputObject
    }
}

function Get-VoiceList {
    if ([string]::IsNullOrWhiteSpace($VoicesJson)) {
        return $voices
    }
    if (-not (Test-Path $VoicesJson)) {
        throw "VoicesJson not found: $VoicesJson"
    }
    $raw = Get-Content -LiteralPath $VoicesJson -Raw -Encoding UTF8
    $parsed = ConvertFrom-Json -InputObject $raw
    if ($parsed.voices) {
        $parsed = $parsed.voices
    }
    if ($parsed -isnot [System.Collections.IEnumerable] -or $parsed -is [string]) {
        throw "VoicesJson must be a JSON array, or an object with a voices array."
    }
    $list = @()
    foreach ($item in $parsed) {
        $voice = ConvertTo-Hashtable $item
        foreach ($required in @("Token", "Name", "Lang", "Gender", "Voice", "Endpoint", "Path")) {
            if (-not $voice.ContainsKey($required) -or [string]::IsNullOrWhiteSpace([string]$voice[$required])) {
                throw "Voice entry is missing required field '$required'."
            }
        }
        $list += ,$voice
    }
    return $list
}

function Write-VoiceToken {
    param([hashtable] $Voice)

    $tokenPath = Join-Path $Sapi5Root $Voice.Token
    if (Test-Path $tokenPath) {
        # Idempotent: overwrite attributes, keep token id.
    } else {
        New-Item -Path $tokenPath -Force | Out-Null
    }
    Set-Item -Path $tokenPath -Value $Voice.Name
    New-ItemProperty -Path $tokenPath -Name "CLSID" -Value $Clsid -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $tokenPath -Name $Voice.Lang.TrimStart("0") -Value $Voice.Name -PropertyType String -Force | Out-Null

    $legacyClsidSubkey = Join-Path $tokenPath "CLSID"
    if (Test-Path $legacyClsidSubkey) {
        Remove-Item -Path $legacyClsidSubkey -Recurse -Force
    }

    $attrPath = Join-Path $tokenPath "Attributes"
    if (-not (Test-Path $attrPath)) {
        New-Item -Path $attrPath -Force | Out-Null
    }
    @{
        Name      = $Voice.Name
        Language  = $Voice.Lang
        Gender    = $Voice.Gender
        Age       = "Adult"
        Vendor    = $Vendor
        VoiceName = $Voice.Name
        Provider  = if ($Voice.ContainsKey("Provider")) { $Voice.Provider } else { "edge" }
        Model     = if ($Voice.ContainsKey("Model")) { $Voice.Model } else { "" }
        Endpoint  = $Voice.Endpoint
        Path      = $Voice.Path
        VoiceId   = $Voice.Voice
        Rate      = if ($Voice.ContainsKey("Rate")) { $Voice.Rate } else { "1.0" }
        TimeoutMs = if ($Voice.ContainsKey("TimeoutMs")) { $Voice.TimeoutMs } else { "15000" }
    }.GetEnumerator() | ForEach-Object {
        New-ItemProperty -Path $attrPath -Name $_.Key -Value $_.Value -PropertyType String -Force | Out-Null
    }
}

function Remove-VoiceToken {
    param([string] $Token)
    $tokenPath = Join-Path $Sapi5Root $Token
    if (Test-Path $tokenPath) {
        Remove-Item -Path $tokenPath -Recurse -Force
    }
}

function Show-InstalledVoices {
    Add-Type -AssemblyName System.Speech -ErrorAction Stop
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        Write-Host ""
        Write-Host "Installed SAPI5 voices that match the engine CLSID:" -ForegroundColor Cyan
        $found = $false
        foreach ($v in $synth.GetInstalledVoices()) {
            if (-not $v.Enabled) { continue }
            # System.Speech doesn't expose the CLSID, so we match by name
            # prefix instead. Adjust if you change $Vendor / Name format.
            if ($v.VoiceInfo.Name -like "MinimaxTTS - *") {
                $line = " - {0,-8} {1}" -f $v.VoiceInfo.Culture.Name, $v.VoiceInfo.Name
                Write-Host $line -ForegroundColor Green
                $found = $true
            }
        }
        if (-not $found) {
            Write-Host " (none yet - check registration errors above)" -ForegroundColor Yellow
        }
    } finally {
        $synth.Dispose()
    }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

Assert-Administrator
$voiceList = Get-VoiceList

if ($Unregister) {
    Write-Host "Unregistering MinimaxTTS SAPI5 voices and COM class..." -ForegroundColor Yellow
    foreach ($v in $voiceList) {
        Remove-VoiceToken -Token $v.Token
    }
    $clsidRoot = "${Hive}:\SOFTWARE\Classes\CLSID\$Clsid"
    if (Test-Path $clsidRoot) {
        Remove-Item -Path $clsidRoot -Recurse -Force
    }
    Write-Host "Done. Voice tokens and COM registration removed." -ForegroundColor Green
    return
}

# 1) Make sure the DLL exists. If it isn't at the default path but is
# available locally, fall back to the build output so registration works
# without copying the DLL to Program Files.
if (-not (Test-Path $DllPath)) {
    $fallback = Join-Path $PSScriptRoot "http_sapi5_engine.dll"
    if (Test-Path $fallback) {
        Write-Warning "DLL not at $DllPath; falling back to $fallback"
        $DllPath = $fallback
    } else {
        throw "Engine DLL not found at $DllPath or $fallback. Build it first (see README.md)."
    }
}

# 2) Register the COM class. We write the same keys the engine's
# DllRegisterServer would, straight into the chosen hive. HKCU gives a
# per-user registration; HKLM gives a system-wide registration.
Write-Host "Registering COM class $Clsid for $DllPath ..." -ForegroundColor Cyan
$clsidRoot = "${Hive}:\SOFTWARE\Classes\CLSID\$Clsid"
New-Item -Path $clsidRoot -Force | Out-Null
Set-Item -Path $clsidRoot -Value "MinimaxTTS SAPI5 HTTP Bridge"

$inproc = "$clsidRoot\InprocServer32"
New-Item -Path $inproc -Force | Out-Null
Set-Item -Path $inproc -Value $DllPath
Set-ItemProperty -Path $inproc -Name "ThreadingModel" -Value "Both"
Write-Host ("  + CLSID\{0}\InprocServer32 -> {1}" -f $Clsid, $DllPath) -ForegroundColor DarkGray

# 3) Write one registry voice token per entry
foreach ($v in $voiceList) {
    Write-VoiceToken -Voice $v
    Write-Host ("  + {0,-30} {1} -> {2}{3}" -f $v.Token, $v.Name, $v.Endpoint, $v.Path)
}

# 4) Light-touch verification: ask the local server if it's reachable
$sample = $voiceList | Select-Object -First 1
if ($sample) {
    $ok = Test-LocalServerReachable -Endpoint $sample.Endpoint -Path $sample.Path
    if ($ok) {
        Write-Host ("Local server at {0}{1} responded." -f $sample.Endpoint, $sample.Path) -ForegroundColor Green
    } else {
        Write-Warning ("Local server at {0}{1} did NOT respond. SAPI5 voices will be silent until the server is running." -f $sample.Endpoint, $sample.Path)
    }
}

# 5) Friendly list of what's installed
Show-InstalledVoices

Write-Host ""
Write-Host "Done. Restart Zotero so it re-enumerates SAPI5 voices." -ForegroundColor Green

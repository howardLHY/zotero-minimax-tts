# SAPI5 Bridge

This directory contains the native Windows SAPI5 engine used by Zotero MiniMax TTS.

The engine is a COM DLL. Windows loads it as a SAPI5 voice, then the engine forwards each `Speak()` request to a local HTTP endpoint and writes WAV PCM back to SAPI5.

## Runtime Flow

```text
Zotero / Windows TTS / SAPI5 app
        |
        v
Windows SAPI5
        |
        v
http_sapi5_engine.dll
        |
        v
POST http://127.0.0.1:5050/v1/speech
        |
        v
MiniMax / OpenAI-compatible / local TTS provider
```

The endpoint must return RIFF/WAVE audio when the request asks for:

```json
{
  "input": "text to speak",
  "voice": "<VOICE_ID>",
  "response_format": "wav"
}
```

## Files

| File | Purpose |
| --- | --- |
| `http_sapi5_engine.cpp` | Native C++ SAPI5 engine. |
| `http_sapi5_engine.def` | Exports COM entry points. |
| `sapi5_shim.h` | Small compatibility wrapper for SAPI headers. |
| `build.bat` | Builds the x64 DLL with Visual Studio Build Tools. |
| `register-sapi5-voices.ps1` | Registers the COM class and voice tokens. |
| `voices.example.json` | Example voice catalog. Replace placeholder voice ids. |

## Build

Requirements:

- Windows 10/11
- Visual Studio 2022 Build Tools
- Desktop development with C++
- Windows SDK

Build:

```cmd
cd C:\Path\To\zotero-minimax-tts\tools\sapi5
build.bat
```

Output:

```text
http_sapi5_engine.dll
```

Keep the DLL in a stable location before registering voices, for example:

```text
C:\Path\To\MinimaxTTS\sapi5\http_sapi5_engine.dll
```

## Voice Catalog

Use `voices.example.json` as a template.

Important fields:

| Field | Notes |
| --- | --- |
| `Token` | Unique registry token id. |
| `Name` | Display name shown by SAPI5 clients. |
| `Lang` | LCID such as `0409`, `0804`, `0411`. |
| `Gender` | `Male`, `Female`, or `Neutral`. Metadata only. |
| `Provider` | Metadata passed through to the local server. |
| `Model` | Provider model id. |
| `Voice` | Provider voice id sent as JSON `voice`. |
| `Endpoint` | Local server base URL, usually `http://127.0.0.1:5050`. |
| `Path` | Local server path, usually `/v1/speech`. |
| `Rate` | Base speed multiplier. |
| `TimeoutMs` | Request timeout for this voice token. |

Language examples:

```text
0409 = English (United States)
0804 = Chinese (Simplified, China)
0411 = Japanese
```

## Register Voices

Per-user registration:

```powershell
.\register-sapi5-voices.ps1 `
  -Hive HKCU `
  -DllPath "C:\Path\To\http_sapi5_engine.dll" `
  -VoicesJson "C:\Path\To\voices.json"
```

System-wide registration:

```powershell
.\register-sapi5-voices.ps1 `
  -Hive HKLM `
  -DllPath "C:\Path\To\http_sapi5_engine.dll" `
  -VoicesJson "C:\Path\To\voices.json"
```

Run the HKLM command from an Administrator PowerShell.

The manager writes a temporary helper script under the current user's app-data directory and launches it elevated when you click `REGISTER SAPI5`.

## Verify

```powershell
Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$s.GetInstalledVoices() | ForEach-Object { $_.VoiceInfo.Name }
$s.SelectVoice("MinimaxTTS - Example Voice")
$s.Speak("hello from sapi five")
```

If the voice appears in Windows TTS but not in Zotero, restart Zotero. Zotero also groups voices by language, so check the English, Chinese, or Japanese voice list according to the token's LCID.

## Performance Notes

- SAPI5 calls this engine synchronously.
- Remote TTS latency can make the calling app feel paused until audio returns.
- Keep the manager running while using these voices.
- Enable cache in the manager to reduce repeated sentence latency.
- First sentence latency is usually provider/TLS/model warmup.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Voice registers but is silent | Manager is not running, endpoint path is wrong, or provider returned non-WAV audio. |
| Zotero cannot see the voice | Restart Zotero and check the correct language list. |
| Registration window closes too fast | Run the generated helper script from an Administrator PowerShell. |
| Port already in use | Stop the old manager/server or choose another port and re-register voices. |

## Security

Do not put API keys in SAPI5 registry tokens. Keep secrets in the manager config or provider environment only.

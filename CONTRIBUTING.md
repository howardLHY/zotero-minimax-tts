# Contributing

Thanks for helping improve MiniMax SAPI5 TTS Bridge.

## Development Setup

1. Clone the repository:

   ```powershell
   git clone https://github.com/howardLHY/zotero-minimax-tts.git
   cd zotero-minimax-tts
   ```

2. Build the Windows manager with a local C# compiler:

   ```powershell
   & "C:\Path\To\csc.exe" `
     /target:winexe /platform:x64 `
     /out:tools\manager\MinimaxTTSManager.exe `
     /r:System.dll `
     /r:System.Core.dll `
     /r:System.Drawing.dll `
     /r:System.Windows.Forms.dll `
     /r:System.Web.Extensions.dll `
     tools\manager\MinimaxTTSManager.cs
   ```

3. Build the SAPI5 bridge:

   ```cmd
   cd tools\sapi5
   build.bat
   ```

## Pull Request Guidelines

- Keep provider secrets out of the repository.
- Use placeholder paths such as `C:\Path\To\...`, `<USER_HOME>\...`, or `<LOCAL_REPO_PATH>\...`.
- Do not commit generated binaries, release ZIPs, WAV files, logs, local config, or cache directories.
- Keep UI changes tested by launching the manager once.
- Keep SAPI5 changes tested with both `System.Speech` and at least one SAPI5 client.

## Coding Notes

- `tools/manager/MinimaxTTSManager.cs` targets .NET Framework WinForms.
- `tools/sapi5/http_sapi5_engine.cpp` is plain C++/COM and avoids ATL.
- The SAPI5 bridge expects WAV bytes for SAPI5 playback.
- Zotero groups SAPI5 voices by LCID, so voice language metadata matters.

## Secret Scan

Before opening a PR:

```powershell
rg -n -P "s[k]-|api[_-]?key|MINIMAX[_]API[_]KEY=(?!<)|YOUR[_]REAL[_]KEY" `
  -g "!node_modules/**" `
  -g "!build/**" `
  -g "!*.exe" `
  -g "!*.dll" `
  -g "!*.zip"
```

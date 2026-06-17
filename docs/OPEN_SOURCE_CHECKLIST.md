# Open Source Release Checklist

Use this before creating the public GitHub repository or publishing a release.

## Repository Hygiene

- [x] Replaced `YOUR_NAME` in `package.json`, `README.md`, and docs with `howardLHY`.
- [x] Replaced `security@example.com` in `SECURITY.md` with `hongyuflow@outlook.com`.
- [ ] Confirm `.env` is not committed.
- [ ] Confirm generated files are not committed:
  - `*.exe`
  - `*.dll`
  - `*.zip`
  - `*.wav`
  - `*.log`
  - `tools/manager-package/`

## Secret Scan

Run:

```powershell
rg -n -P "s[k]-|MINIMAX[_]API[_]KEY=(?!<)|YOUR[_]REAL[_]KEY|<YOUR_REAL_WINDOWS_USER>|<YOUR_REAL_LOCAL_PATH>" `
  -g "!node_modules/**" `
  -g "!build/**" `
  -g "!*.exe" `
  -g "!*.dll" `
  -g "!*.zip" `
  -g "!*.wav" `
  -g "!*.log"
```

Expected result: no real secrets and no personal paths.

## Build Release ZIP

The release ZIP should contain only:

```text
MinimaxTTSManager.exe
README.md
sapi5/http_sapi5_engine.dll
sapi5/register-sapi5-voices.ps1
sapi5/voices.example.json
```

Upload the ZIP to GitHub Releases. Do not commit it to the source tree.

## Smoke Test

- [ ] Manager opens.
- [ ] Service starts.
- [ ] Test voice returns audio.
- [ ] `voices.json` is generated.
- [ ] SAPI5 registration completes.
- [ ] Windows `System.Speech` can enumerate the voice.
- [ ] Zotero can see the voice after restart and in the correct language list.

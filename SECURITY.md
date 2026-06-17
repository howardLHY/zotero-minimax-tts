# Security Policy

## Supported Versions

This project is pre-1.0. Security fixes target the latest public release.

## Reporting A Vulnerability

Please report security issues privately before opening a public issue.

Contact: hongyuflow@outlook.com

Include:

- affected version or commit
- reproduction steps
- expected impact
- whether any API key, token, or local registry value is exposed

## Secret Handling

- Never commit API keys.
- Never publish local config files.
- Never publish personal paths such as `<USER_HOME>\...` or `<LOCAL_REPO_PATH>\...`.
- Use placeholders in documentation:

  ```text
  <YOUR_API_KEY>
  <YOUR_PROVIDER_BASE_URL>
  C:\Path\To\MinimaxTTS
  ```

## Local Network Surface

The manager starts a local HTTP server on loopback by default. Keep it bound to `127.0.0.1` unless you understand the risk of exposing a TTS endpoint on your network.

## Registry Writes

SAPI5 registration writes Windows registry voice tokens. Review `tools/sapi5/register-sapi5-voices.ps1` before running it as Administrator.

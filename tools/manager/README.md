# MinimaxTTSManager

`MinimaxTTSManager.exe` is the Windows GUI for this project. It configures a TTS provider, runs the local HTTP bridge, generates `voices.json`, and launches the SAPI5 registration script.

## User Flow

1. Run `MinimaxTTSManager.exe`.
2. Choose a provider.
3. Fill in:

   ```text
   API Key: <YOUR_API_KEY>
   Base URL: <YOUR_PROVIDER_BASE_URL>
   Model: <YOUR_TTS_MODEL>
   Voice ID: <YOUR_DEFAULT_VOICE_ID>
   ```

4. Select preset voices or add custom voice ids.
5. Click `START`.
6. Click `REGISTER SAPI5`.
7. Restart Zotero or any other app that needs to re-enumerate SAPI5 voices.

## Advanced Settings

The advanced panel contains:

- local port
- request timeout
- max concurrent synthesis count
- cache size
- `voices.json` path
- SAPI5 DLL path
- registration script path

Most users do not need to change these.

## Provider Notes

- MiniMax uses its native `/v1/t2a_v2` request format.
- GLM, OpenAI Compatible, and Custom TTS use an OpenAI-compatible `/audio/speech` request format.

## Local Files

The manager stores user configuration under the current user's application data directory. Do not copy that private config into the public repository.

Use placeholders in public docs:

```text
C:\Path\To\MinimaxTTS
<YOUR_API_KEY>
<YOUR_PROVIDER_BASE_URL>
```

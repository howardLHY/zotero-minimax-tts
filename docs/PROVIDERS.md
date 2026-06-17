# TTS Provider Notes

The manager supports two request styles.

## MiniMax Native

Provider:

```text
MiniMax
```

Request shape:

```text
POST <BASE_URL>/v1/t2a_v2
Authorization: Bearer <YOUR_API_KEY>
```

Public documentation should use a placeholder base URL:

```text
https://api.example.com
```

Users should replace it with the endpoint from their provider console or official docs.

## OpenAI-Compatible

Providers:

```text
GLM / OpenAI Compatible
OpenAI Compatible
Custom TTS
```

Request shape:

```text
POST <BASE_URL>/audio/speech
Authorization: Bearer <YOUR_API_KEY>
```

Body:

```json
{
  "model": "<YOUR_TTS_MODEL>",
  "input": "text to speak",
  "voice": "<YOUR_VOICE_ID>",
  "response_format": "wav",
  "speed": 1.0
}
```

## Voice IDs

Voice IDs are provider-specific. They are not invented by this project.

For MiniMax, examples include:

```text
English_compelling_lady1
English_radiant_girl
English_expressive_narrator
English_magnetic_voiced_man
Chinese (Mandarin)_Reliable_Executive
Chinese (Mandarin)_News_Anchor
Japanese_IntellectualSenior
Japanese_DecisivePrincess
```

For other providers, use the voice IDs from that provider's documentation.

## Local Endpoint

The SAPI5 voice tokens should point to the local manager, not directly to the remote provider:

```text
Endpoint: http://127.0.0.1:5050
Path: /v1/speech
```

This keeps API keys out of the Windows registry.

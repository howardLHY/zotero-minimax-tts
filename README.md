# MiniMax SAPI5 TTS Bridge

> 把 MiniMax、OpenAI 兼容 TTS、本地 Edge TTS 等语音服务注册成 Windows SAPI5 语音，让 Zotero / Word / Windows 朗读器等所有支持 SAPI5 的应用直接调用这些声音。

<p align="center">
  <a href="https://github.com/howardLHY/zotero-minimax-tts/releases"><img alt="Release" src="https://img.shields.io/github/v/release/howardLHY/zotero-minimax-tts?include_prereleases&style=flat-square"></a>
  <a href="https://github.com/howardLHY/zotero-minimax-tts/blob/main/LICENSE"><img alt="License" src="https://img.shields.io/github/license/howardLHY/zotero-minimax-tts?style=flat-square"></a>
  <a href="https://github.com/howardLHY/zotero-minimax-tts/stargazers"><img alt="Stars" src="https://img.shields.io/github/stars/howardLHY/zotero-minimax-tts?style=flat-square"></a>
  <a href="https://github.com/howardLHY/zotero-minimax-tts/issues"><img alt="Issues" src="https://img.shields.io/github/issues/howardLHY/zotero-minimax-tts?style=flat-square"></a>
  <a href="https://github.com/howardLHY/zotero-minimax-tts/releases/latest"><img alt="Download" src="https://img.shields.io/github/downloads/howardLHY/zotero-minimax-tts/total?style=flat-square"></a>
</p>

<p align="center">
  <b>Windows-only</b> · <b>Python 3.9+</b> optional local server · <b>MIT License</b>
</p>

---

## 📑 目录

1. [这是做什么的](#-1-这是做什么的)
2. [效果展示](#-2-效果展示)
3. [工作原理](#-3-工作原理)
4. [快速开始](#-4-快速开始)
5. [详细使用指南](#-5-详细使用指南)
6. [支持的 TTS Provider](#-6-支持的-tts-provider)
7. [从源码构建](#-7-从源码构建)
8. [故障排查](#-8-故障排查)
9. [项目结构](#-9-项目结构)
10. [安全 & 隐私](#-10-安全--隐私)
11. [贡献](#-11-贡献)
12. [License](#-12-license)

---

## 🎯 1. 这是做什么的

**MiniMax SAPI5 TTS Bridge** 是一个 Windows 工具，把云端 / 本地 TTS 服务的"音色"，**注册成 Windows 系统的 SAPI5 语音**。注册之后：

- ✅ **Zotero 阅读器**：在 `阅读 → 朗读` 列表里多出 MiniMax / OpenAI-兼容的音色
- ✅ **Microsoft Word**：`审阅 → 朗读` 直接选这些声音
- ✅ **Windows 朗读器** (`Win + Ctrl + Enter`)：Edge 自带的"讲述人"也能用
- ✅ **任何 SAPI5 应用**：`System.Speech.Synthesis.SpeechSynthesizer.GetInstalledVoices()` 都能枚举到

> 💡 **为什么是 SAPI5 而不是浏览器插件？** 因为 SAPI5 是 Windows 操作系统级接口——一个注册就全系统生效，不需要每个软件单独装插件。

---

## ✨ 2. 效果展示

注册后的 SAPI5 音色在 Zotero 朗读下拉里会按 **语言（LCID）** 自动分组：

| 语言分组 | 出现的音色示例 |
| --- | --- |
| English (United States, 0409) | `MinimaxTTS - English radiant girl`、`MinimaxTTS - English narrator` |
| Chinese (Simplified, 0804) | `MinimaxTTS - Mandarin news anchor`、`MinimaxTTS - Chinese executive` |
| Japanese (0411) | `MinimaxTTS - Japanese princess`、`MinimaxTTS - Japanese senior` |

> 💡 同一个 provider 音色可以用不同 LCID 注册多次，**在多个语言列表下都能找到**。例如 `English_radiant_girl` 既可以注册到 0409（英文）也可以注册到 0804（中文），用同一个声音读不同语言的文本。

---

## 🧠 3. 工作原理

```text
┌──────────────────────────────────────────────────────────┐
│ Zotero / Word / 任意 SAPI5 应用                          │
│      ↓ Speak("text", voice)                              │
│ Windows SAPI5 子系统                                       │
│      ↓ 查找 voice token                                   │
│ http_sapi5_engine.dll (COM Inproc Server)                │
│      ↓ POST /v1/speech                                   │
│ 本地 127.0.0.1:5050 (MinimaxTTSManager.exe 内置 HTTP)     │
│      ↓ 携带真实 API Key                                   │
│ MiniMax T2A / OpenAI 兼容 / Edge TTS Provider            │
│      ↓ 返回 WAV / MP3                                    │
│ 逐段写入 SAPI5 音频流                                      │
└──────────────────────────────────────────────────────────┘
```

**关键设计**：**API key 永远不出本机**。它只存在 manager 进程的内存里 + `%APPDATA%\MinimaxTTS\config.json`，不写进 Windows 注册表，朗读时由本地 HTTP 服务用本机持有的 key 调用远端 provider。

---

## 🚀 4. 快速开始

### 4.1 系统要求

- **Windows 10 / 11**（x64）
- **可选**：Python 3.9+（只有用 Edge TTS 路径才需要）
- **管理员权限**（仅在 HKLM 全局注册时需要；HKCU 用户级注册不需要）

### 4.2 三步上手

#### Step 1 · 下载发布包

去 [Releases](https://github.com/howardLHY/zotero-minimax-tts/releases) 下载最新 ZIP，假设解压到：

```text
C:\Tools\MinimaxTTS\
```

解压后目录结构：

```text
C:\Tools\MinimaxTTS\
├── MinimaxTTSManager.exe
└── sapi5\
    ├── http_sapi5_engine.dll
    ├── register-sapi5-voices.ps1
    └── voices.example.json
```

#### Step 2 · 启动管理起并填配置

双击 `MinimaxTTSManager.exe`，在窗口里填入：

| 字段 | MiniMax 示例 | OpenAI 兼容示例 |
| --- | --- | --- |
| Provider | `MiniMax` | `OpenAI Compatible` |
| API Key | 你的 MiniMax 控制台 key | 你 provider 的 key |
| Base URL | `https://api.minimaxi.com` | `https://tts-provider.example.com/v1` |
| Model | `speech-2.8-hd` | `tts-1` |
| Voice ID | `English_radiant_girl` | `<PROVIDER_VOICE_ID>` |

> 🌱 不知道怎么填？展开下面的"参数详解"。

<details>
<summary>📋 <b>参数详解（点击展开）</b></summary>

- **Provider**：四种预设
  - `MiniMax` — 使用 MiniMax 原生 `/v1/t2a_v2` 协议
  - `GLM / OpenAI Compatible` — 智谱 GLM 风格
  - `OpenAI Compatible` — 通用 OpenAI `/v1/audio/speech`
  - `Custom TTS` — 自定义 endpoint
- **API Key**：直接到 provider 控制台拿，**只在本机内存 + APPDATA** 里存
- **Base URL**：完整到 `/v1` 这一层即可，路径 `/audio/speech` 会自动拼
- **Model**：
  - MiniMax：`speech-2.8-hd` / `speech-2.8-turbo` / `speech-2.6-hd` / `speech-01-hd`
  - Edge：`tts-1`
  - 其他 provider：以该 provider 文档为准
- **Voice ID**：MiniMax 音色 ID（如 `English_radiant_girl`），其他 provider 同

</details>

#### Step 3 · 启动 + 注册 + 重启 Zotero

1. 在管理起里点 **▶ START**（底部状态条会变成绿色"运行中"）
2. 点 **⚙ REGISTER SAPI5**
3. 弹出 UAC 提示时点"是"（HKLM 全局）或"否"（HKCU 用户级，脚本会回退）
4. **关闭 Zotero 后重新打开**（Zotero 启动时才枚举 SAPI5 语音）
5. 打开任意 PDF → `阅读 → 朗读 → 选 MiniMax - <你的音色>`

🎉 **完成！现在 Zotero 用 MiniMax 的声音给你朗读论文。**

---

## 📘 5. 详细使用指南

### 5.1 高级设置（展开管理起右下角"Advanced"）

| 项 | 默认值 | 说明 |
| --- | --- | --- |
| Local Port | `5050` | 本地 HTTP 服务端口 |
| Timeout (ms) | `30000` | 单次合成最长等待 |
| Max Concurrent | `2` | 并发请求数（防止 provider 限流） |
| Cache Entries | `64` | LRU 缓存条数（按 text+voice+model+rate 键） |
| Voices JSON | `%APPDATA%\MinimaxTTS\voices.json` | 生成的注册清单 |
| SAPI5 DLL | 同目录或 `sapi5\` 下 | COM DLL 路径 |
| Register Script | 同目录或 `sapi5\` 下 | PS1 注册脚本路径 |

### 5.2 自定义音色（"CUSTOM" 区）

在 **CUSTOM** 文本框里按以下格式一行一条添加：

```text
<Display Name>|<voice_id>|<LCID hex>|<Gender>
```

示例：

```text
My custom voice|English_radiant_girl|0409|Female
Same voice but Chinese|English_radiant_girl|0804|Female
Japanese cool voice|Japanese_IntellectualSenior|0411|Male
```

> 💡 LCID 是 Windows 用来分类语言的 16 进制编码，常见值：
> `0409`=英文(美) `0804`=简体中文 `0411`=日本语 `0C04`=繁体中文 `040C`=法语 `0407`=德语

### 5.3 不想要图形界面？纯命令行

跳过管理起，直接用 PowerShell 注册：

```powershell
cd C:\Tools\MinimaxTTS\sapi5

# 1. 编辑 voices.example.json，填入你的 voice 列表（必填字段：Token / Name / Lang / Gender / Voice / Endpoint / Path）
Copy-Item .\voices.example.json .\voices.json
notepad .\voices.json

# 2. 用户级注册（不需要管理员）
.\register-sapi5-voices.ps1 `
  -Hive HKCU `
  -DllPath "C:\Tools\MinimaxTTS\sapi5\http_sapi5_engine.dll" `
  -VoicesJson "C:\Tools\MinimaxTTS\sapi5\voices.json"

# 3. 系统级注册（需要管理员）
Start-Process powershell -Verb RunAs -ArgumentList @(
  "-NoProfile","-ExecutionPolicy","Bypass",
  "-File","C:\Tools\MinimaxTTS\sapi5\register-sapi5-voices.ps1",
  "-Hive","HKLM",
  "-DllPath","C:\Tools\MinimaxTTS\sapi5\http_sapi5_engine.dll",
  "-VoicesJson","C:\Tools\MinimaxTTS\sapi5\voices.json"
)
```

### 5.4 用 Python 本地服务（可选）

如果只想用 Edge TTS 不想写 MiniMax 代码，可以跑预置的 Python 服务：

```powershell
pip install flask edge-tts miniaudio
python tools\openai_edge_tts_server.py --host 127.0.0.1 --port 5050
```

然后在管理起里 Provider 选 `OpenAI Compatible`，Base URL 填 `http://127.0.0.1:5050`，Voice ID 填 Edge 音色名（如 `zh-CN-YunxiNeural`）。

### 5.5 验证注册成功

```powershell
Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$s.GetInstalledVoices() | Where-Object { $_.VoiceInfo.Name -like "MinimaxTTS - *" } |
    ForEach-Object { "{0,-10} {1}" -f $_.VoiceInfo.Culture.Name, $_.VoiceInfo.Name }
```

期望输出：

```text
zh-CN      MinimaxTTS - Mandarin news anchor
en-US      MinimaxTTS - English radiant girl
ja-JP      MinimaxTTS - Japanese princess
```

---

## 🌐 6. 支持的 TTS Provider

| Provider | 协议 | 备注 |
| --- | --- | --- |
| **MiniMax** | `POST /v1/t2a_v2` | 官方 T2A v2 接口，支持情绪、语速、音调、文本规范化 |
| **GLM / OpenAI Compatible** | `POST /v1/audio/speech` | 智谱 GLM 风格 |
| **OpenAI Compatible** | `POST /v1/audio/speech` | 任何实现 OpenAI TTS 协议的 endpoint |
| **Custom TTS** | `POST <your>/<path>` | 完全自定义 |
| **Edge TTS**（通过 Python） | 内部 | 离线本地；不消耗 API key |

MiniMax 音色 ID 速查（不全，更多请查 provider 文档）：

```text
English_radiant_girl
English_compelling_lady1
English_expressive_narrator
English_magnetic_voiced_man
Chinese (Mandarin)_Reliable_Executive
Chinese (Mandarin)_News_Anchor
Japanese_IntellectualSenior
Japanese_DecisivePrincess
```

> 📝 完整 provider 说明：[docs/PROVIDERS.md](docs/PROVIDERS.md)

---

## 🔨 7. 从源码构建

### 7.1 构建 Manager（WinForms EXE）

需求：.NET Framework 4.x + `csc.exe`

```powershell
& "C:\Path\To\csc.exe" `
  /nologo /utf8output /codepage:65001 `
  /target:winexe /platform:x64 /optimize+ `
  /out:tools\manager\MinimaxTTSManager.exe `
  /r:System.dll `
  /r:System.Core.dll `
  /r:System.Drawing.dll `
  /r:System.Windows.Forms.dll `
  /r:System.Web.Extensions.dll `
  tools\manager\MinimaxTTSManager.cs
```

或直接：

```cmd
npm run build:manager
```

### 7.2 构建 SAPI5 DLL（COM）

需求：VS 2022 Build Tools + "Desktop development with C++" + Windows SDK

```cmd
cd tools\sapi5
build.bat
```

或：

```cmd
npm run build:sapi5
```

输出：`tools\sapi5\http_sapi5_engine.dll`

> 🔍 这个 DLL 故意不用 ATL — ATL 在新版 VS 里把 `DllGetClassObject` 改成 inline，没法被 COM 通过 GetProcAddress 找到。所以四个 `Dll*` 入口是手写的，并靠 `.def` 文件暴露符号。

---

## 🛠 8. 故障排查

<details>
<summary><b>❓ 装了但 Zotero 列表里没有这个声音</b></summary>

- **Zotero 没重启**：Zotero 启动时才枚举 SAPI5 音色，必须关闭后再打开
- **没在正确的语言列表下**：SAPI5 声音按 LCID 分组，进 英文 / 中文 / 日文 三个列表分别找
- **HKCU 没生效**：打开 `regedit` 检查 `HKEY_CURRENT_USER\SOFTWARE\Microsoft\Speech\Voices\Tokens` 下有没有 `MinimaxTTS-*` 开头的项

</details>

<details>
<summary><b>❓ 选了声音但没有声音 / 沉默</b></summary>

- **本地服务没运行**：打开管理起，确认状态条是"运行中"
- **API key 错误**：在管理起里点 Test，验证 key 有效
- **provider 返回了非 WAV**：MiniMax 必须传 `response_format=wav`，SAPI5 不接受 MP3
- **端口冲突**：换了端口后没重新注册 SAPI5 token（token 里的 Endpoint 写死了）

</details>

<details>
<summary><b>❓ UAC 弹窗点了"否"，之后没动静</b></summary>

注册脚本默认会同时写 HKLM（系统）和 HKCU（用户）。如果 UAC 拒绝：
- HKCU 部分**已经写入**（不需要管理员）
- HKLM 部分需要你手动以管理员身份重跑：

```powershell
Start-Process powershell -Verb RunAs -ArgumentList @(
  "-NoProfile","-ExecutionPolicy","Bypass",
  "-File","C:\Tools\MinimaxTTS\sapi5\register-sapi5-voices.ps1",
  "-Hive","HKLM",
  "-DllPath","C:\Tools\MinimaxTTS\sapi5\http_sapi5_engine.dll",
  "-VoicesJson","C:\Tools\MinimaxTTS\sapi5\voices.json"
)
```

</details>

<details>
<summary><b>❓ 想卸载干净</b></summary>

1. 关闭管理起（停止本地服务）
2. 重启 Zotero（释放 SAPI5 句柄）
3. 跑卸载：

```powershell
# 用户级卸载
& C:\Tools\MinimaxTTS\sapi5\register-sapi5-voices.ps1 -Unregister -Hive HKCU

# 系统级卸载（管理员）
Start-Process powershell -Verb RunAs -ArgumentList @(
  "-NoProfile","-ExecutionPolicy","Bypass",
  "-File","C:\Tools\MinimaxTTS\sapi5\register-sapi5-voices.ps1",
  "-Unregister","-Hive","HKLM"
)

# 删除本地配置（API key 存在这里）
Remove-Item -Recurse "$env:APPDATA\MinimaxTTS"
```

</details>

<details>
<summary><b>❓ 端口 5050 被占用</b></summary>

管理起 → Advanced → 把 Local Port 改成 `5051` 等，然后重跑注册脚本（生成的 `voices.json` 里的 Endpoint 会跟着变）。

</details>

---

## 📁 9. 项目结构

```text
zotero-minimax-tts/
├── README.md                          ← 你正在看
├── LICENSE                            ← MIT
├── CHANGELOG.md                       ← 版本变更记录
├── CONTRIBUTING.md                    ← 贡献指南
├── SECURITY.md                        ← 安全策略
├── .env.example                       ← 环境变量示例
├── .gitignore
├── package.json                       ← npm scripts: build:manager / build:sapi5
├── docs/
│   ├── PROVIDERS.md                   ← 详细 provider 说明
│   └── OPEN_SOURCE_CHECKLIST.md       ← 发布前自检表
└── tools/
    ├── openai_edge_tts_server.py      ← 可选 Python 本地服务
    ├── manager/
    │   ├── MinimaxTTSManager.cs       ← WinForms 管理起（含本地 HTTP server）
    │   └── README.md
    └── sapi5/
        ├── http_sapi5_engine.cpp      ← SAPI5 COM 引擎
        ├── http_sapi5_engine.def      ← 导出符号
        ├── sapi5_shim.h               ← SAPI 头兼容层
        ├── build.bat                  ← 构建脚本
        ├── register-sapi5-voices.ps1  ← 注册 / 卸载脚本
        ├── voices.example.json        ← 音色清单模板
        └── README.md
```

---

## 🔒 10. 安全 & 隐私

- 🚫 **绝不** 把 API key commit 到仓库
- 🚫 **绝不** 把 API key 写进 Windows 注册表（它只存在 `%APPDATA%\MinimaxTTS\config.json`）
- 🚫 **绝不** 在公开 issue / discussion 里贴 API key 或注册表 dump
- ✅ 本地 HTTP 服务只绑定 `127.0.0.1`，**不会**对局域网开放
- ✅ 注册表 token 里只存 endpoint/voice metadata，**不存** key

> 📝 完整安全策略：[SECURITY.md](SECURITY.md) · 如何报告漏洞：`hongyuflow@outlook.com`

---

## 🤝 11. 贡献

欢迎 PR！流程：

1. Fork + clone
2. 按 [CONTRIBUTING.md](CONTRIBUTING.md) 里的步骤构建
3. 提交 PR 前跑一次 secret scan：

   ```powershell
   rg -n -P "s[k]-|api[_-]?key|MINIMAX[_]API[_]KEY=(?!<)|YOUR[_]REAL[_]KEY" `
     -g "!node_modules/**" -g "!build/**" `
     -g "!*.exe" -g "!*.dll" -g "!*.zip"
   ```

   应该**没有命中**。

---

## ⭐ Star History

如果你觉得这个工具有用，欢迎点 Star ⭐ — 这是继续维护的最大动力。

---

## 📄 12. License

MIT — see [LICENSE](LICENSE).

Copyright © 2026 howardLHY

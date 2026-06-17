// http_sapi5_engine.cpp - raw C++ SAPI5 TTS engine.
//
// Build (x64, from an "x64 Native Tools Command Prompt for VS 2022"):
//   cl /nologo /EHsc /LD /O2 /D _WINDLL http_sapi5_engine.cpp ^
//      /link /DEF:http_sapi5_engine.def ^
//      winhttp.lib ole32.lib advapi32.lib oleaut32.lib
//
// Uses raw COM. ATL's atls.lib in VS 2026 does not provide DllGetClassObject
// as an exported symbol, so we hand-write the four Dll* entry points. The
// .def file ensures they are visible to SAPI5 via GetProcAddress.

#define _WIN32_DCOM
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <objbase.h>
#include <combaseapi.h>

#include <winerror.h>
#include <sapi.h>
#include "sapi5_shim.h"
#include <winhttp.h>
#include <string>
#include <vector>
#include <cstdio>
#include <cstdlib>
#include <cmath>

#pragma comment(lib, "winhttp.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(lib, "oleaut32.lib")

// {72A7DE4B-327C-4EB8-A402-A280F4F41A05}
static const GUID CLSID_HttpSapi5Engine = {
    0x72a7de4b, 0x327c, 0x4eb8,
    { 0xa4, 0x02, 0xa2, 0x80, 0xf4, 0xf4, 0x1a, 0x05 }
};

static LONG     g_DllRefCount = 0;
static LONG     g_LockCount   = 0;
static HMODULE  g_hModule     = nullptr;
// ---------------------------------------------------------------------------


// helpers
// ---------------------------------------------------------------------------
static bool DebugEnabled() {
    static int enabled = -1;
    if (enabled >= 0) return enabled == 1;
    wchar_t value[16] = {};
    DWORD n = GetEnvironmentVariableW(L"MINIMAXTTS_SAPI_DEBUG", value, 16);
    enabled = (n > 0 && value[0] && value[0] != L'0') ? 1 : 0;
    return enabled == 1;
}

static std::wstring LogPath() {
    wchar_t custom[MAX_PATH] = {};
    DWORD n = GetEnvironmentVariableW(L"MINIMAXTTS_SAPI_LOG", custom, MAX_PATH);
    if (n > 0 && n < MAX_PATH) return custom;

    wchar_t temp[MAX_PATH] = {};
    DWORD len = GetTempPathW(MAX_PATH, temp);
    if (len == 0 || len >= MAX_PATH) return L"minimaxtts_sapi5.log";
    return std::wstring(temp) + L"minimaxtts_sapi5.log";
}

static void Log(const std::wstring& msg) {
    if (!DebugEnabled()) return;
    std::wstring path = LogPath();
    HANDLE h = CreateFileW(path.c_str(),
        FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr,
        OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (h == INVALID_HANDLE_VALUE) return;
    SYSTEMTIME st;
    GetLocalTime(&st);
    wchar_t prefix[64];
    swprintf_s(prefix, L"%04u-%02u-%02u %02u:%02u:%02u ",
        st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond);
    std::wstring line = std::wstring(prefix) + msg + L"\r\n";
    int n = WideCharToMultiByte(CP_UTF8, 0, line.c_str(), (int)line.size(),
        nullptr, 0, nullptr, nullptr);
    std::string utf8(n, 0);
    WideCharToMultiByte(CP_UTF8, 0, line.c_str(), (int)line.size(),
        &utf8[0], n, nullptr, nullptr);
    DWORD written = 0;
    WriteFile(h, utf8.data(), (DWORD)utf8.size(), &written, nullptr);
    CloseHandle(h);
}

static std::wstring ReadTokenString(ISpObjectToken *tok, LPCWSTR name) {
    WCHAR *v = nullptr;
    if (SUCCEEDED(tok->GetStringValue(name, &v)) && v) {
        std::wstring out(v);
        CoTaskMemFree(v);
        return out;
    }
    if (v) CoTaskMemFree(v);

    ISpDataKey *attrs = nullptr;
    if (SUCCEEDED(tok->OpenKey(L"Attributes", &attrs)) && attrs) {
        v = nullptr;
        if (SUCCEEDED(attrs->GetStringValue(name, &v)) && v) {
            std::wstring out(v);
            CoTaskMemFree(v);
            attrs->Release();
            return out;
        }
        if (v) CoTaskMemFree(v);
        attrs->Release();
    }
    return L"";
}

static std::string JsonEscape(const std::wstring& in) {
    int n = WideCharToMultiByte(CP_UTF8, 0, in.c_str(), (int)in.size(),
                                nullptr, 0, nullptr, nullptr);
    std::string utf8(n, 0);
    WideCharToMultiByte(CP_UTF8, 0, in.c_str(), (int)in.size(),
                        &utf8[0], n, nullptr, nullptr);
    std::string out; out.reserve(utf8.size() + 8);
    for (size_t i = 0; i < utf8.size(); ++i) {
        unsigned char c = (unsigned char)utf8[i];
        switch (c) {
            case '"':  out += "\\\""; break;
            case '\\': out += "\\\\"; break;
            case '\b': out += "\\b";  break;
            case '\f': out += "\\f";  break;
            case '\n': out += "\\n";  break;
            case '\r': out += "\\r";  break;
            case '\t': out += "\\t";  break;
            default:
                if (c < 0x20) {
                    char buf[8];
                    std::snprintf(buf, sizeof(buf), "\\u%04x", c);
                    out += buf;
                } else {
                    out += (char)c;
                }
        }
    }
    return out;
}

static bool HttpPost(const std::wstring& endpoint,
                     const std::wstring& path,
                     const std::wstring& provider,
                     const std::wstring& model,
                     const std::wstring& voiceId,
                     const std::wstring& text,
                     float speed,
                     DWORD timeoutMs,
                     std::vector<unsigned char>& outBody) {
    std::wstring fullUrl = endpoint + path;
    Log(L"HttpPost url=" + fullUrl + L" voice=" + voiceId + L" textLen=" + std::to_wstring(text.size()));

    URL_COMPONENTS uc = { sizeof(uc) };
    wchar_t host[256]   = {};
    wchar_t urlPath[2048] = {};
    uc.lpszHostName    = host;    uc.dwHostNameLength  = 256;
    uc.lpszUrlPath     = urlPath; uc.dwUrlPathLength   = 2048;
    if (!WinHttpCrackUrl(fullUrl.c_str(), (DWORD)fullUrl.size(), 0, &uc)) {
        Log(L"HttpPost WinHttpCrackUrl failed");
        return false;
    }

    HINTERNET hSession = WinHttpOpen(L"MinimaxTTS-SAPI5/1.0",
        WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, WINHTTP_NO_PROXY_NAME,
        WINHTTP_NO_PROXY_BYPASS, 0);
    if (!hSession) {
        Log(L"HttpPost WinHttpOpen failed");
        return false;
    }
    DWORD connectTimeout = timeoutMs < 5000 ? timeoutMs : 5000;
    WinHttpSetTimeouts(hSession, 2000, connectTimeout, timeoutMs, timeoutMs);

    HINTERNET hConnect = WinHttpConnect(hSession, host, uc.nPort, 0);
    if (!hConnect) { Log(L"HttpPost WinHttpConnect failed"); WinHttpCloseHandle(hSession); return false; }

    DWORD flags = (uc.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
    HINTERNET hReq = WinHttpOpenRequest(hConnect, L"POST", urlPath,
        nullptr, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
    if (!hReq) { Log(L"HttpPost WinHttpOpenRequest failed"); WinHttpCloseHandle(hConnect); WinHttpCloseHandle(hSession); return false; }

    std::wstring headers = L"Content-Type: application/json; charset=utf-8\r\n";
    std::string body = "{\"input\":\"" + JsonEscape(text) +
                       "\",\"voice\":\"" + JsonEscape(voiceId) +
                       "\",\"response_format\":\"wav\",\"speed\":" +
                       std::to_string((double)speed);
    if (!provider.empty()) body += ",\"provider\":\"" + JsonEscape(provider) + "\"";
    if (!model.empty()) body += ",\"model\":\"" + JsonEscape(model) + "\"";
    body += "}";

    BOOL ok = WinHttpSendRequest(hReq, headers.c_str(), (DWORD)headers.size(),
                                 (LPVOID)body.data(), (DWORD)body.size(),
                                 (DWORD)body.size(), 0);
    if (!ok) { Log(L"HttpPost WinHttpSendRequest failed"); goto fail; }
    ok = WinHttpReceiveResponse(hReq, nullptr);
    if (!ok) { Log(L"HttpPost WinHttpReceiveResponse failed"); goto fail; }

    DWORD status = 0;
    DWORD statusSize = sizeof(status);
    if (WinHttpQueryHeaders(hReq,
            WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
            WINHTTP_HEADER_NAME_BY_INDEX, &status, &statusSize,
            WINHTTP_NO_HEADER_INDEX)) {
        if (status < 200 || status >= 300) {
            Log(L"HttpPost status=" + std::to_wstring(status));
            goto fail;
        }
    }

    for (;;) {
        DWORD avail = 0;
        if (!WinHttpQueryDataAvailable(hReq, &avail) || avail == 0) break;
        std::vector<unsigned char> buf(avail);
        DWORD got = 0;
        if (!WinHttpReadData(hReq, buf.data(), avail, &got) || got == 0) break;
        outBody.insert(outBody.end(), buf.begin(), buf.begin() + got);
        if (outBody.size() > 50 * 1024 * 1024) {
            Log(L"HttpPost response too large");
            goto fail;
        }
    }

    WinHttpCloseHandle(hReq);
    WinHttpCloseHandle(hConnect);
    WinHttpCloseHandle(hSession);
    Log(L"HttpPost response bytes=" + std::to_wstring(outBody.size()));
    return !outBody.empty();

fail:
    WinHttpCloseHandle(hReq);
    WinHttpCloseHandle(hConnect);
    WinHttpCloseHandle(hSession);
    return false;
}

static bool StripWav(const std::vector<unsigned char>& wav,
                     std::vector<unsigned char>& pcm,
                     WAVEFORMATEX* outFmt) {
    Log(L"StripWav input bytes=" + std::to_wstring(wav.size()));
    if (wav.size() < 44) return false;
    if (memcmp(&wav[0], "RIFF", 4) != 0) return false;
    if (memcmp(&wav[8], "WAVE", 4) != 0) return false;

    size_t pos = 12;
    bool gotFmt = false, gotData = false;
    while (pos + 8 <= wav.size()) {
        DWORD chunkSize = *(DWORD*)&wav[pos + 4];
        if (memcmp(&wav[pos], "fmt ", 4) == 0 && chunkSize >= 16) {
            outFmt->wFormatTag      = *(WORD*)&wav[pos + 8];
            outFmt->nChannels       = *(WORD*)&wav[pos + 10];
            outFmt->nSamplesPerSec  = *(DWORD*)&wav[pos + 12];
            outFmt->nAvgBytesPerSec = *(DWORD*)&wav[pos + 16];
            outFmt->nBlockAlign     = *(WORD*)&wav[pos + 20];
            outFmt->wBitsPerSample  = *(WORD*)&wav[pos + 22];
            outFmt->cbSize          = 0;
            gotFmt = true;
        } else if (memcmp(&wav[pos], "data", 4) == 0) {
            if (pos + 8 + chunkSize > wav.size()) return false;
            pcm.assign(wav.begin() + pos + 8, wav.begin() + pos + 8 + chunkSize);
            gotData = true;
        }
        size_t advance = 8 + chunkSize + (chunkSize & 1);
        if (advance < 8) return false;
        pos += advance;
    }
    Log(L"StripWav gotFmt=" + std::to_wstring(gotFmt ? 1 : 0) +
        L" gotData=" + std::to_wstring(gotData ? 1 : 0) +
        L" pcmBytes=" + std::to_wstring(pcm.size()));
    return gotFmt && gotData;
}

static bool ShouldAbort(ISpTTSEngineSite *site) {
    if (!site) return true;
    return (site->GetActions() & SPVES_ABORT) != 0;
}

static float ClampSpeed(float speed) {
    if (speed < 0.25f) return 0.25f;
    if (speed > 4.0f) return 4.0f;
    return speed;
}

static float RateAdjustToSpeed(long rateAdjust) {
    if (rateAdjust < -10) rateAdjust = -10;
    if (rateAdjust > 10) rateAdjust = 10;
    return (float)std::pow(2.0, (double)rateAdjust / 10.0);
}

// ---------------------------------------------------------------------------
// SAPI5 TTS engine - single vtable matching the SAPI 5.4 layout.
// Derives from ISpTTSEngine first, then ISpObjectWithToken. SAPI5 queries
// IUnknown -> ISpTTSEngine; SetObjectToken is on ISpObjectWithToken.
// ---------------------------------------------------------------------------
class CHttpSapi5Engine :
    public ISpTTSEngine,
    public ISpObjectWithToken
{
public:
    CHttpSapi5Engine() : m_refCount(1), m_pToken(nullptr), m_speed(1.0f), m_timeoutMs(15000) {
        InterlockedIncrement(&g_DllRefCount);
        Log(L"Engine ctor");
    }
    virtual ~CHttpSapi5Engine() {
        Log(L"Engine dtor");
        if (m_pToken) m_pToken->Release();
        InterlockedDecrement(&g_DllRefCount);
    }

    // ISpTTSEngine
    STDMETHODIMP Speak(DWORD, REFGUID, const WAVEFORMATEX*,
                       const SPVTEXTFRAG *pText, ISpTTSEngineSite *pOut);
    STDMETHODIMP GetOutputFormat(const GUID*, const WAVEFORMATEX*,
                                 GUID*, WAVEFORMATEX **ppFmt);

    // ISpObjectWithToken
    STDMETHODIMP SetObjectToken(ISpObjectToken *pToken);
    STDMETHODIMP GetObjectToken(ISpObjectToken **ppToken);

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID riid, LPVOID *ppv);
    STDMETHODIMP_(ULONG) AddRef();
    STDMETHODIMP_(ULONG) Release();

private:
    LONG             m_refCount;
    ISpObjectToken  *m_pToken;
    std::wstring     m_endpoint;
    std::wstring     m_path;
    std::wstring     m_provider;
    std::wstring     m_model;
    std::wstring     m_voiceId;
    float            m_speed;
    DWORD            m_timeoutMs;
};

STDMETHODIMP_(ULONG) CHttpSapi5Engine::AddRef() {
    return (ULONG)InterlockedIncrement(&m_refCount);
}

STDMETHODIMP_(ULONG) CHttpSapi5Engine::Release() {
    LONG r = InterlockedDecrement(&m_refCount);
    if (r == 0) delete this;
    return (ULONG)r;
}

STDMETHODIMP CHttpSapi5Engine::QueryInterface(REFIID riid, LPVOID *ppv) {
    Log(L"Engine QI");
    if (!ppv) return E_POINTER;
    if (riid == IID_IUnknown) {
        *ppv = static_cast<ISpTTSEngine*>(this);
    } else if (riid == __uuidof(ISpTTSEngine)) {
        *ppv = static_cast<ISpTTSEngine*>(this);
    } else if (riid == __uuidof(ISpObjectWithToken)) {
        *ppv = static_cast<ISpObjectWithToken*>(this);
    } else {
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    AddRef();
    return S_OK;
}

STDMETHODIMP CHttpSapi5Engine::SetObjectToken(ISpObjectToken *pToken) {
    Log(L"SetObjectToken enter");
    if (!pToken) return E_POINTER;
    if (m_pToken) m_pToken->Release();
    m_pToken = pToken; m_pToken->AddRef();

    m_endpoint = ReadTokenString(m_pToken, L"Endpoint");
    if (m_endpoint.empty()) m_endpoint = L"http://127.0.0.1:5050";
    m_path     = ReadTokenString(m_pToken, L"Path");
    if (m_path.empty()) m_path = L"/v1/speech";
    m_provider = ReadTokenString(m_pToken, L"Provider");
    m_model    = ReadTokenString(m_pToken, L"Model");
    m_voiceId  = ReadTokenString(m_pToken, L"VoiceId");
    std::wstring rateStr = ReadTokenString(m_pToken, L"Rate");
    if (!rateStr.empty()) {
        m_speed = ClampSpeed((float)_wtof(rateStr.c_str()));
    }
    std::wstring timeoutStr = ReadTokenString(m_pToken, L"TimeoutMs");
    if (!timeoutStr.empty()) {
        int timeout = _wtoi(timeoutStr.c_str());
        if (timeout < 3000) timeout = 3000;
        if (timeout > 120000) timeout = 120000;
        m_timeoutMs = (DWORD)timeout;
    }
    Log(L"SetObjectToken endpoint=" + m_endpoint + L" path=" + m_path +
        L" provider=" + m_provider + L" model=" + m_model +
        L" voice=" + m_voiceId + L" timeoutMs=" + std::to_wstring(m_timeoutMs));
    return S_OK;
}

STDMETHODIMP CHttpSapi5Engine::GetObjectToken(ISpObjectToken **ppToken) {
    Log(L"GetObjectToken");
    if (!ppToken) return E_POINTER;
    *ppToken = m_pToken;
    if (m_pToken) m_pToken->AddRef();
    return S_OK;
}

STDMETHODIMP CHttpSapi5Engine::GetOutputFormat(
    const GUID*, const WAVEFORMATEX*,
    GUID *pOutputFormatId, WAVEFORMATEX **ppFmt)
{
    Log(L"GetOutputFormat enter");
    if (!ppFmt) return E_POINTER;
    WAVEFORMATEX *p = (WAVEFORMATEX*)CoTaskMemAlloc(sizeof(WAVEFORMATEX));
    if (!p) return E_OUTOFMEMORY;
    ZeroMemory(p, sizeof(*p));
    p->wFormatTag      = WAVE_FORMAT_PCM;
    p->nChannels       = 1;
    p->nSamplesPerSec  = 22050;
    p->nAvgBytesPerSec = 22050 * 2;
    p->nBlockAlign     = 2;
    p->wBitsPerSample  = 16;
    p->cbSize          = 0;
    if (pOutputFormatId) *pOutputFormatId = SPDFID_WaveFormatEx;
    *ppFmt = p;
    Log(L"GetOutputFormat ok");
    return S_OK;
}

STDMETHODIMP CHttpSapi5Engine::Speak(
    DWORD, REFGUID, const WAVEFORMATEX*,
    const SPVTEXTFRAG *pText, ISpTTSEngineSite *pOut)
{
    Log(L"Speak enter");
    if (!pText || !pOut) return E_INVALIDARG;

    std::wstring text;
    for (auto *f = pText; f; f = f->pNext) {
        if (f->pTextStart && f->ulTextLen) text.append(f->pTextStart, f->ulTextLen);
    }
    Log(L"Speak textLen=" + std::to_wstring(text.size()));
    if (text.empty()) return S_OK;
    if (ShouldAbort(pOut)) return S_OK;

    long siteRate = 0;
    if (FAILED(pOut->GetRate(&siteRate))) siteRate = 0;
    long weightedFragRate = 0;
    ULONG weightedChars = 0;
    for (auto *f = pText; f; f = f->pNext) {
        if (!f->ulTextLen) continue;
        weightedFragRate += f->State.RateAdj * (long)f->ulTextLen;
        weightedChars += f->ulTextLen;
    }
    long fragRate = weightedChars ? (weightedFragRate / (long)weightedChars) : 0;
    float effectiveSpeed = ClampSpeed(m_speed * RateAdjustToSpeed(siteRate + fragRate));
    Log(L"Speak rate site=" + std::to_wstring(siteRate) +
        L" frag=" + std::to_wstring(fragRate) +
        L" speed=" + std::to_wstring((double)effectiveSpeed));

    std::vector<unsigned char> wav;
    if (!HttpPost(m_endpoint, m_path, m_provider, m_model, m_voiceId, text,
                  effectiveSpeed, m_timeoutMs, wav)) {
        Log(L"Speak HttpPost failed");
        return E_FAIL;
    }
    if (ShouldAbort(pOut)) return S_OK;

    std::vector<unsigned char> pcm;
    WAVEFORMATEX fmt = {};
    if (!StripWav(wav, pcm, &fmt)) {
        Log(L"Speak StripWav failed");
        return E_FAIL;
    }
    if (fmt.wFormatTag != WAVE_FORMAT_PCM ||
        fmt.nSamplesPerSec != 22050 ||
        fmt.nChannels != 1 ||
        fmt.wBitsPerSample != 16) {
        Log(L"Speak unsupported wav fmt");
        return E_FAIL;
    }

    const size_t CHUNK = 32768;
    for (size_t off = 0; off < pcm.size(); off += CHUNK) {
        if (ShouldAbort(pOut)) {
            Log(L"Speak abort");
            return S_OK;
        }
        size_t n = (pcm.size() - off < CHUNK) ? (pcm.size() - off) : CHUNK;
        ULONG written = 0;
        HRESULT hr = pOut->Write(pcm.data() + off, (ULONG)n, &written);
        if (FAILED(hr)) {
            Log(L"Speak Write failed hr=" + std::to_wstring((long)hr));
            return hr;
        }
    }
    Log(L"Speak ok");
    return S_OK;
}

// ---------------------------------------------------------------------------
// Class factory
// ---------------------------------------------------------------------------
class CHttpSapi5ClassFactory : public IClassFactory {
public:
    CHttpSapi5ClassFactory() : m_refCount(1) {
        InterlockedIncrement(&g_DllRefCount);
        Log(L"Factory ctor");
    }
    virtual ~CHttpSapi5ClassFactory() {
        Log(L"Factory dtor");
        InterlockedDecrement(&g_DllRefCount);
    }

    STDMETHODIMP QueryInterface(REFIID riid, LPVOID *ppv) {
        Log(L"Factory QI");
        if (!ppv) return E_POINTER;
        if (riid == IID_IUnknown || riid == IID_IClassFactory) {
            *ppv = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() {
        return (ULONG)InterlockedIncrement(&m_refCount);
    }
    STDMETHODIMP_(ULONG) Release() {
        LONG r = InterlockedDecrement(&m_refCount);
        if (r == 0) delete this;
        return (ULONG)r;
    }
    STDMETHODIMP CreateInstance(IUnknown *pOuter, REFIID riid, LPVOID *ppv) {
        Log(L"Factory CreateInstance");
        if (pOuter) return CLASS_E_NOAGGREGATION;
        CHttpSapi5Engine *e = new CHttpSapi5Engine();
        if (!e) return E_OUTOFMEMORY;
        HRESULT hr = e->QueryInterface(riid, ppv);
        e->Release();
        return hr;
    }
    STDMETHODIMP LockServer(BOOL fLock) {
        if (fLock) InterlockedIncrement(&g_LockCount);
        else       InterlockedDecrement(&g_LockCount);
        return S_OK;
    }
private:
    LONG m_refCount;
};


// ---------------------------------------------------------------------------
// DLL exports - the .def file lists these so COM can find them by name.
// DllGetClassObject and DllCanUnloadNow are already declared by the Windows
// SDK, so define them with the matching STDAPI linkage and let the .def file
// handle exporting.
// ---------------------------------------------------------------------------
extern "C" BOOL APIENTRY DllMain(
    HMODULE h, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) {
        g_hModule = h;
        DisableThreadLibraryCalls(h);
    }
    return TRUE;
}

STDAPI DllGetClassObject(
    REFCLSID rclsid, REFIID riid, LPVOID *ppv)
{
    if (rclsid != CLSID_HttpSapi5Engine) return 0x80040154; // CLASS_E_CLASSNOTREG
    CHttpSapi5ClassFactory *f = new CHttpSapi5ClassFactory();
    if (!f) return E_OUTOFMEMORY;
    HRESULT hr = f->QueryInterface(riid, ppv);
    f->Release();
    return hr;
}

STDAPI DllCanUnloadNow() {
    return (g_DllRefCount == 0 && g_LockCount == 0) ? S_OK : S_FALSE;
}

STDAPI DllRegisterServer() {
    wchar_t clsid[64];
    StringFromGUID2(CLSID_HttpSapi5Engine, clsid, 64);
    wchar_t dllPath[MAX_PATH];
    GetModuleFileNameW(g_hModule, dllPath, MAX_PATH);

    HKEY hKey = nullptr;
    std::wstring base = L"CLSID\\" + std::wstring(clsid);

    if (RegCreateKeyExW(HKEY_CLASSES_ROOT, base.c_str(), 0, nullptr,
            REG_OPTION_NON_VOLATILE, KEY_WRITE, nullptr, &hKey, nullptr) != ERROR_SUCCESS) {
        return E_FAIL;
    }
    LPCWSTR name = L"MinimaxTTS SAPI5 HTTP Bridge";
    RegSetValueExW(hKey, nullptr, 0, REG_SZ,
        (BYTE*)name, (DWORD)(wcslen(name) + 1) * 2);
    RegCloseKey(hKey);

    std::wstring inproc = base + L"\\InprocServer32";
    if (RegCreateKeyExW(HKEY_CLASSES_ROOT, inproc.c_str(), 0, nullptr,
            REG_OPTION_NON_VOLATILE, KEY_WRITE, nullptr, &hKey, nullptr) != ERROR_SUCCESS) {
        return E_FAIL;
    }
    RegSetValueExW(hKey, nullptr, 0, REG_SZ,
        (BYTE*)dllPath, (DWORD)(wcslen(dllPath) + 1) * 2);
    LPCWSTR model = L"Both";
    RegSetValueExW(hKey, L"ThreadingModel", 0, REG_SZ,
        (BYTE*)model, (DWORD)(wcslen(model) + 1) * 2);
    RegCloseKey(hKey);
    return S_OK;
}

STDAPI DllUnregisterServer() {
    wchar_t clsid[64];
    StringFromGUID2(CLSID_HttpSapi5Engine, clsid, 64);
    std::wstring base = L"CLSID\\" + std::wstring(clsid);
    RegDeleteTreeW(HKEY_CLASSES_ROOT, base.c_str());
    return S_OK;
}

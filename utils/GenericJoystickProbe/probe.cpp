#define DIRECTINPUT_VERSION 0x0800
#include <windows.h>
#include <mmsystem.h>
#include <dinput.h>
#include <iostream>
#include <string>
#include <cstdlib>
#include <array>
#include <vector>
#include <cstdint>
#include <algorithm>

// Read-only consumer. Never installs, attaches, hides or removes any device.
static bool experimental(WORD vid, WORD pid) {
    return vid == 0xFFFF && (pid == 0xD10C || pid == 0xD110);
}
static IDirectInput8W* input = nullptr;
static int duration = 0;
static int found = 0;
static bool failed = false;
static bool jsonMode = false;
struct Reading {
    std::wstring name;
    std::array<LONG, 6> axes{};
    DWORD buttons = 0;
    int hat = 8;
    DWORD axisCount = 0, buttonCount = 0, hatCount = 0;
};
static std::vector<Reading> diReadings, mmReadings;
static int hatIndex(DWORD value) { return LOWORD(value) == 0xFFFF ? 8 : static_cast<int>(value / 4500); }
static LONG normalized(DWORD value, UINT min, UINT max) {
    if (max <= min) { failed = true; return 0; }
    double unit = (static_cast<double>(value) - min) / (max - min);
    return static_cast<LONG>(std::clamp(unit, 0.0, 1.0) * 65534.0 - 32767.0);
}
static void quote(const std::wstring& value) {
    std::wcout << L'"';
    for (wchar_t c : value) {
        if (c == L'"' || c == L'\\') std::wcout << L'\\' << c;
        else if (c >= 32 && c < 127) std::wcout << c;
        else { const wchar_t* hex = L"0123456789abcdef"; std::wcout << L"\\u" << hex[(c >> 12) & 15] << hex[(c >> 8) & 15] << hex[(c >> 4) & 15] << hex[c & 15]; }
    }
    std::wcout << L'"';
}
static void printReadings(const std::vector<Reading>& values) {
    std::wcout << L"[";
    bool first = true;
    for (const auto& value : values) {
        if (!first) std::wcout << L","; first = false;
        std::wcout << L"{\"name\":"; quote(value.name);
        std::wcout << L",\"axes\":[";
        for (size_t i = 0; i < 6; ++i) { if (i) std::wcout << L","; std::wcout << value.axes[i]; }
        std::wcout << L"],\"buttons\":" << value.buttons << L",\"hat\":" << value.hat
            << L",\"axisCount\":" << value.axisCount << L",\"buttonCount\":" << value.buttonCount
            << L",\"hatCount\":" << value.hatCount << L"}";
    }
    std::wcout << L"]";
}

static BOOL CALLBACK objects(const DIDEVICEOBJECTINSTANCEW* object, VOID*) {
    std::wcout << L"  object\t" << object->tszName << L"\toffset=" << object->dwOfs
        << L"\ttype=" << std::hex << object->dwType << std::dec << L"\n";
    return DIENUM_CONTINUE;
}

static BOOL CALLBACK devices(const DIDEVICEINSTANCEW* instance, VOID*) {
    if (!experimental(LOWORD(instance->guidProduct.Data1), HIWORD(instance->guidProduct.Data1)))
        return DIENUM_CONTINUE;
    ++found;
    if (!jsonMode) std::wcout << L"DirectInput\t" << instance->tszProductName << L"\n";
    IDirectInputDevice8W* device = nullptr;
    HRESULT result = input->CreateDevice(instance->guidInstance, &device, nullptr);
    if (FAILED(result)) { failed = true; std::wcerr << L"CreateDevice failed: " << std::hex << result << L"\n"; return DIENUM_CONTINUE; }
    DIDEVCAPS caps{}; caps.dwSize = sizeof(caps);
    result = device->GetCapabilities(&caps);
    if (SUCCEEDED(result) && !jsonMode)
        std::wcout << L"  axes=" << caps.dwAxes << L" buttons=" << caps.dwButtons
            << L" hats=" << caps.dwPOVs << L" forceFeedback=" << ((caps.dwFlags & DIDC_FORCEFEEDBACK) != 0) << L"\n";
    if (FAILED(result)) failed = true;
    if (!jsonMode && FAILED(device->EnumObjects(objects, nullptr, DIDFT_ALL))) failed = true;
    HWND window = CreateWindowExW(0, L"STATIC", L"VIIPER read-only probe", WS_POPUP,
        0, 0, 1, 1, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    if (!window) { failed = true; device->Release(); return DIENUM_CONTINUE; }
    result = device->SetDataFormat(&c_dfDIJoystick);
    if (SUCCEEDED(result)) result = device->SetCooperativeLevel(window, DISCL_BACKGROUND | DISCL_NONEXCLUSIVE);
    DIPROPRANGE range{}; range.diph.dwSize = sizeof(range);
    range.diph.dwHeaderSize = sizeof(range.diph); range.diph.dwHow = DIPH_DEVICE;
    range.lMin = -32767; range.lMax = 32767;
    if (SUCCEEDED(result)) result = device->SetProperty(DIPROP_RANGE, &range.diph);
    if (SUCCEEDED(result)) result = device->Acquire();
    if (FAILED(result)) { failed = true; std::wcerr << L"DirectInput setup failed: " << std::hex << result << L"\n"; DestroyWindow(window); device->Release(); return DIENUM_CONTINUE; }
    ULONGLONG until = GetTickCount64() + duration * 1000ULL;
    do {
        device->Poll();
        DIJOYSTATE state{};
        result = device->GetDeviceState(sizeof(state), &state);
        if (FAILED(result)) { failed = true; std::wcerr << L"GetDeviceState failed: " << std::hex << result << L"\n"; break; }
        Reading reading;
        reading.name = instance->tszProductName;
        reading.axes = { state.lX, state.lY, state.lZ, state.lRx, state.lRy, state.lRz };
        for (int i = 0; i < 32; ++i) if (state.rgbButtons[i] & 0x80) reading.buttons |= 1u << i;
        reading.hat = hatIndex(state.rgdwPOV[0]);
        reading.axisCount = caps.dwAxes; reading.buttonCount = caps.dwButtons; reading.hatCount = caps.dwPOVs;
        diReadings.push_back(reading);
        if (!jsonMode) {
        std::wcout << L"DI\tX=" << state.lX << L" Y=" << state.lY << L" Z=" << state.lZ
            << L" Rx=" << state.lRx << L" Ry=" << state.lRy << L" Rz=" << state.lRz
            << L" POV=" << state.rgdwPOV[0] << L" buttons=";
        for (int i = 0; i < 32; ++i) if (state.rgbButtons[i] & 0x80) std::wcout << i + 1 << L",";
        std::wcout << L"\n";
        }
        if (duration) Sleep(100);
    } while (GetTickCount64() < until);
    device->Unacquire(); DestroyWindow(window); device->Release();
    return DIENUM_CONTINUE;
}

int wmain(int argc, wchar_t** argv) {
    jsonMode = argc == 2 && std::wstring(argv[1]) == L"--json";
    if (argc > 2 || (argc == 2 && !jsonMode && (std::wstring(argv[1]).find_first_not_of(L"0123456789") != std::wstring::npos))) {
        std::wcerr << L"Usage: GenericJoystickProbe.exe [watch-seconds: 0..120]\n"; return 1;
    }
    duration = argc == 2 ? _wtoi(argv[1]) : 0;
    if (duration > 120) { std::wcerr << L"Watch limit is 120 seconds.\n"; return 1; }
    if (!jsonMode) std::wcout << L"Read-only VIIPER experimental joystick probe (" << sizeof(void*) * 8 << L"-bit)\n";
    int mmFound = 0;
    for (UINT id = 0; id < joyGetNumDevs(); ++id) {
        JOYCAPSW caps{};
        if (joyGetDevCapsW(id, &caps, sizeof(caps)) != JOYERR_NOERROR || !experimental(caps.wMid, caps.wPid)) continue;
        ++mmFound;
        if (!jsonMode) std::wcout << L"WinMM\tid=" << id << L" name=" << caps.szPname
            << L" axes=" << caps.wNumAxes << L" buttons=" << caps.wNumButtons
            << L" caps=" << std::hex << caps.wCaps << std::dec << L"\n";
        JOYINFOEX state{}; state.dwSize = sizeof(state); state.dwFlags = JOY_RETURNALL;
        MMRESULT result = joyGetPosEx(id, &state);
        if (result == JOYERR_NOERROR) {
            Reading reading;
            reading.name = caps.szPname;
            reading.axes = { normalized(state.dwXpos, caps.wXmin, caps.wXmax), normalized(state.dwYpos, caps.wYmin, caps.wYmax),
                normalized(state.dwZpos, caps.wZmin, caps.wZmax), normalized(state.dwUpos, caps.wUmin, caps.wUmax),
                normalized(state.dwVpos, caps.wVmin, caps.wVmax), normalized(state.dwRpos, caps.wRmin, caps.wRmax) };
            reading.buttons = state.dwButtons; reading.hat = hatIndex(state.dwPOV);
            reading.axisCount = caps.wNumAxes; reading.buttonCount = caps.wNumButtons; reading.hatCount = (caps.wCaps & JOYCAPS_HASPOV) ? 1 : 0;
            mmReadings.push_back(reading);
            if (!jsonMode)
            std::wcout << L"MM\tX=" << state.dwXpos << L" Y=" << state.dwYpos << L" Z=" << state.dwZpos
                << L" R=" << state.dwRpos << L" U=" << state.dwUpos << L" V=" << state.dwVpos
                << L" POV=" << state.dwPOV << L" buttons=" << state.dwButtons << L"\n";
        } else { failed = true; std::wcerr << L"joyGetPosEx failed: " << result << L"\n"; }
    }
    HRESULT result = DirectInput8Create(GetModuleHandleW(nullptr), DIRECTINPUT_VERSION,
        IID_IDirectInput8W, reinterpret_cast<VOID**>(&input), nullptr);
    if (FAILED(result)) { std::wcerr << L"DirectInput8Create failed: " << std::hex << result << L"\n"; return 1; }
    result = input->EnumDevices(DI8DEVCLASS_GAMECTRL, devices, nullptr, DIEDFL_ATTACHEDONLY);
    input->Release();
    if (FAILED(result)) { std::wcerr << L"EnumDevices failed: " << std::hex << result << L"\n"; return 1; }
    if (jsonMode) {
        std::wcout << L"{\"architecture\":" << sizeof(void*) * 8 << L",\"directInput\":"; printReadings(diReadings);
        std::wcout << L",\"winMM\":"; printReadings(mmReadings);
        std::wcout << L",\"error\":" << (failed ? L"true" : L"false") << L"}\n";
    } else std::wcout << L"Found DirectInput=" << found << L" WinMM=" << mmFound << L"\n";
    return failed ? 1 : (found && mmFound ? 0 : 2);
}

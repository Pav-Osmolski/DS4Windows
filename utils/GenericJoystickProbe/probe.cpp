#define DIRECTINPUT_VERSION 0x0800
#include <windows.h>
#include <mmsystem.h>
#include <dinput.h>
#include <iostream>
#include <string>
#include <cstdlib>

// Read-only consumer. Never installs, attaches, hides or removes any device.
static bool experimental(WORD vid, WORD pid) {
    return vid == 0xFFFF && (pid == 0xD10C || pid == 0xD110);
}
static IDirectInput8W* input = nullptr;
static int duration = 0;
static int found = 0;
static bool failed = false;

static BOOL CALLBACK objects(const DIDEVICEOBJECTINSTANCEW* object, VOID*) {
    std::wcout << L"  object\t" << object->tszName << L"\toffset=" << object->dwOfs
        << L"\ttype=" << std::hex << object->dwType << std::dec << L"\n";
    return DIENUM_CONTINUE;
}

static BOOL CALLBACK devices(const DIDEVICEINSTANCEW* instance, VOID*) {
    if (!experimental(LOWORD(instance->guidProduct.Data1), HIWORD(instance->guidProduct.Data1)))
        return DIENUM_CONTINUE;
    ++found;
    std::wcout << L"DirectInput\t" << instance->tszProductName << L"\n";
    IDirectInputDevice8W* device = nullptr;
    HRESULT result = input->CreateDevice(instance->guidInstance, &device, nullptr);
    if (FAILED(result)) { failed = true; std::wcerr << L"CreateDevice failed: " << std::hex << result << L"\n"; return DIENUM_CONTINUE; }
    DIDEVCAPS caps{}; caps.dwSize = sizeof(caps);
    result = device->GetCapabilities(&caps);
    if (SUCCEEDED(result))
        std::wcout << L"  axes=" << caps.dwAxes << L" buttons=" << caps.dwButtons
            << L" hats=" << caps.dwPOVs << L" forceFeedback=" << ((caps.dwFlags & DIDC_FORCEFEEDBACK) != 0) << L"\n";
    else failed = true;
    if (FAILED(device->EnumObjects(objects, nullptr, DIDFT_ALL))) failed = true;
    result = device->SetDataFormat(&c_dfDIJoystick);
    if (SUCCEEDED(result)) result = device->SetCooperativeLevel(nullptr, DISCL_BACKGROUND | DISCL_NONEXCLUSIVE);
    DIPROPRANGE range{}; range.diph.dwSize = sizeof(range);
    range.diph.dwHeaderSize = sizeof(range.diph); range.diph.dwHow = DIPH_DEVICE;
    range.lMin = -32767; range.lMax = 32767;
    if (SUCCEEDED(result)) result = device->SetProperty(DIPROP_RANGE, &range.diph);
    if (SUCCEEDED(result)) result = device->Acquire();
    if (FAILED(result)) { failed = true; std::wcerr << L"DirectInput setup failed: " << std::hex << result << L"\n"; device->Release(); return DIENUM_CONTINUE; }
    ULONGLONG until = GetTickCount64() + duration * 1000ULL;
    do {
        device->Poll();
        DIJOYSTATE state{};
        result = device->GetDeviceState(sizeof(state), &state);
        if (FAILED(result)) { failed = true; std::wcerr << L"GetDeviceState failed: " << std::hex << result << L"\n"; break; }
        std::wcout << L"DI\tX=" << state.lX << L" Y=" << state.lY << L" Z=" << state.lZ
            << L" Rx=" << state.lRx << L" Ry=" << state.lRy << L" Rz=" << state.lRz
            << L" POV=" << state.rgdwPOV[0] << L" buttons=";
        for (int i = 0; i < 32; ++i) if (state.rgbButtons[i] & 0x80) std::wcout << i + 1 << L",";
        std::wcout << L"\n";
        if (duration) Sleep(100);
    } while (GetTickCount64() < until);
    device->Unacquire(); device->Release();
    return DIENUM_CONTINUE;
}

int wmain(int argc, wchar_t** argv) {
    if (argc > 2 || (argc == 2 && (std::wstring(argv[1]).find_first_not_of(L"0123456789") != std::wstring::npos))) {
        std::wcerr << L"Usage: GenericJoystickProbe.exe [watch-seconds: 0..120]\n"; return 1;
    }
    duration = argc == 2 ? _wtoi(argv[1]) : 0;
    if (duration > 120) { std::wcerr << L"Watch limit is 120 seconds.\n"; return 1; }
    std::wcout << L"Read-only VIIPER experimental joystick probe (" << sizeof(void*) * 8 << L"-bit)\n";
    int mmFound = 0;
    for (UINT id = 0; id < joyGetNumDevs(); ++id) {
        JOYCAPSW caps{};
        if (joyGetDevCapsW(id, &caps, sizeof(caps)) != JOYERR_NOERROR || !experimental(caps.wMid, caps.wPid)) continue;
        ++mmFound;
        std::wcout << L"WinMM\tid=" << id << L" name=" << caps.szPname
            << L" axes=" << caps.wNumAxes << L" buttons=" << caps.wNumButtons
            << L" caps=" << std::hex << caps.wCaps << std::dec << L"\n";
        JOYINFOEX state{}; state.dwSize = sizeof(state); state.dwFlags = JOY_RETURNALL;
        MMRESULT result = joyGetPosEx(id, &state);
        if (result == JOYERR_NOERROR)
            std::wcout << L"MM\tX=" << state.dwXpos << L" Y=" << state.dwYpos << L" Z=" << state.dwZpos
                << L" R=" << state.dwRpos << L" U=" << state.dwUpos << L" V=" << state.dwVpos
                << L" POV=" << state.dwPOV << L" buttons=" << state.dwButtons << L"\n";
        else { failed = true; std::wcerr << L"joyGetPosEx failed: " << result << L"\n"; }
    }
    HRESULT result = DirectInput8Create(GetModuleHandleW(nullptr), DIRECTINPUT_VERSION,
        IID_IDirectInput8W, reinterpret_cast<VOID**>(&input), nullptr);
    if (FAILED(result)) { std::wcerr << L"DirectInput8Create failed: " << std::hex << result << L"\n"; return 1; }
    result = input->EnumDevices(DI8DEVCLASS_GAMECTRL, devices, nullptr, DIEDFL_ATTACHEDONLY);
    input->Release();
    if (FAILED(result)) { std::wcerr << L"EnumDevices failed: " << std::hex << result << L"\n"; return 1; }
    std::wcout << L"Found DirectInput=" << found << L" WinMM=" << mmFound << L"\n";
    return failed ? 1 : (found && mmFound ? 0 : 2);
}

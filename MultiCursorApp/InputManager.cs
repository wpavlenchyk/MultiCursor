using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;

namespace MultiCursorApp
{
    public class InputManager : IDisposable
    {
        // Magic signature to identify OUR injected events in the LL hook.
        private static readonly IntPtr MAGIC_EXTRA_INFO = (IntPtr)0x4D435552; // "MCUR"
        private const uint MAGIC_EXTRA_INFO_U32 = 0x4D435552;

        public bool IsEnabled { get; set; } = false;
        public string PrimaryMouseHandle { get; set; } = "";
        public string SecondaryMouseHandle { get; set; } = "";

        // Secondary cursor position tracked in PHYSICAL SCREEN PIXELS
        private int _secondaryCursorX;
        private int _secondaryCursorY;
        private int _screenLeft, _screenTop, _screenRight, _screenBottom;

        // Events — only for UI rendering updates
        public delegate void SecondaryMouseMovedEventHandler(int screenX, int screenY);
        public event SecondaryMouseMovedEventHandler? SecondaryMouseMoved;

        public delegate void SecondaryMouseClickedEventHandler(bool isDown);
        public event SecondaryMouseClickedEventHandler? SecondaryMouseClicked;

        private Native.LowLevelMouseProc _proc;
        private IntPtr _hookID = IntPtr.Zero;
        private HwndSource _hwndSource;

        public InputManager(Window window)
        {
            _proc = HookCallback;
            IntPtr windowHandle = new WindowInteropHelper(window).Handle;
            _hwndSource = HwndSource.FromHwnd(windowHandle);
            _hwndSource.AddHook(WndProc);

            UpdateScreenBounds();

            // Initialize secondary cursor at center of primary monitor
            _secondaryCursorX = Native.GetSystemMetrics(0) / 2;
            _secondaryCursorY = Native.GetSystemMetrics(1) / 2;

            RegisterRawInput(windowHandle);
            _hookID = SetHook(_proc);
        }

        private void UpdateScreenBounds()
        {
            _screenLeft = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            _screenTop = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            int width = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
            int height = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);
            _screenRight = _screenLeft + width - 1;
            _screenBottom = _screenTop + height - 1;
        }

        public (int x, int y) GetSecondaryCursorScreenPos()
        {
            return (_secondaryCursorX, _secondaryCursorY);
        }

        private void RegisterRawInput(IntPtr hwnd)
        {
            var rid = new Native.RAWINPUTDEVICE[1];
            rid[0].usUsagePage = 0x01;
            rid[0].usUsage = 0x02;
            rid[0].dwFlags = Native.RIDEV_INPUTSINK;
            rid[0].hwndTarget = hwnd;

            if (!Native.RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(rid[0])))
            {
                Debug.WriteLine("Failed to register Raw Input: " + Marshal.GetLastWin32Error());
            }
        }

        private IntPtr SetHook(Native.LowLevelMouseProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule? curModule = curProcess.MainModule)
            {
                return Native.SetWindowsHookEx(Native.WH_MOUSE_LL, proc,
                    Native.GetModuleHandle(curModule!.ModuleName!), 0);
            }
        }

        /// <summary>
        /// LL Hook callback. Blocks all physical mouse events when enabled.
        /// Only lets through events tagged with our MAGIC_EXTRA_INFO.
        /// </summary>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && IsEnabled)
            {
                var hookStruct = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);

                if (hookStruct.dwExtraInfo == MAGIC_EXTRA_INFO)
                {
                    // This is OUR injected event — let it through
                    return Native.CallNextHookEx(_hookID, nCode, wParam, lParam);
                }

                // Block everything else (physical mouse events)
                return (IntPtr)1;
            }
            return Native.CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x00FF) // WM_INPUT
            {
                ProcessRawInput(lParam);
            }
            return IntPtr.Zero;
        }

        private void ProcessRawInput(IntPtr lParam)
        {
            if (!IsEnabled) return;

            uint dwSize = 0;
            uint headerSize = (uint)Marshal.SizeOf(typeof(Native.RAWINPUTHEADER));
            Native.GetRawInputData(lParam, Native.RID_INPUT, IntPtr.Zero, ref dwSize, headerSize);

            if (dwSize == 0) return;

            IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
            try
            {
                int bytesCopied = Native.GetRawInputData(lParam, Native.RID_INPUT, buffer, ref dwSize, headerSize);
                if (bytesCopied <= 0)
                    return;

                var header = Marshal.PtrToStructure<Native.RAWINPUTHEADER>(buffer);

                if (header.dwType != Native.RIM_TYPEMOUSE)
                    return;

                int hdrSize = Marshal.SizeOf(typeof(Native.RAWINPUTHEADER));
                var mouse = Marshal.PtrToStructure<Native.RAWMOUSE>(
                    new IntPtr(buffer.ToInt64() + hdrSize));

                // CRITICAL: Skip our own re-injected events!
                // When we call SendInput, it generates a WM_INPUT with our magic marker
                // in ulExtraInformation. If we process it again, we get an infinite loop.
                if (mouse.ulExtraInformation == MAGIC_EXTRA_INFO_U32)
                    return;

                string hDeviceStr = header.hDevice.ToString();
                int dx = mouse.lLastX;
                int dy = mouse.lLastY;
                ushort buttonFlags = mouse.usButtonFlags;
                short wheelDelta = (short)mouse.usButtonData;

                if (hDeviceStr == SecondaryMouseHandle)
                {
                    HandleSecondaryMouse(dx, dy, buttonFlags);
                }
                else if (hDeviceStr == PrimaryMouseHandle)
                {
                    HandlePrimaryMouse(dx, dy, buttonFlags, wheelDelta);
                }
                // Events from unknown devices are intentionally ignored.
                // The hook already blocked them. This is by design — only
                // the two explicitly assigned mice are processed.
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // --- Injection helpers: all tagged with MAGIC_EXTRA_INFO ---

        private void InjectMouseMove(int dx, int dy)
        {
            var input = new Native.INPUT
            {
                type = Native.INPUT_MOUSE,
                mi = new Native.MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = Native.MOUSEEVENTF_MOVE,
                    dwExtraInfo = MAGIC_EXTRA_INFO
                }
            };
            Native.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        private void InjectMouseButton(uint flags)
        {
            var input = new Native.INPUT
            {
                type = Native.INPUT_MOUSE,
                mi = new Native.MOUSEINPUT
                {
                    dwFlags = flags,
                    dwExtraInfo = MAGIC_EXTRA_INFO
                }
            };
            Native.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        private void InjectMouseWheel(uint flags, short delta)
        {
            var input = new Native.INPUT
            {
                type = Native.INPUT_MOUSE,
                mi = new Native.MOUSEINPUT
                {
                    mouseData = (uint)(int)delta,
                    dwFlags = flags,
                    dwExtraInfo = MAGIC_EXTRA_INFO
                }
            };
            Native.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        // --- Primary mouse: re-inject everything so it works normally ---

        private void HandlePrimaryMouse(int dx, int dy, ushort buttonFlags, short wheelDelta)
        {
            // Re-inject movement
            if (dx != 0 || dy != 0)
                InjectMouseMove(dx, dy);

            // Re-inject button events
            if ((buttonFlags & Native.RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
                InjectMouseButton(Native.MOUSEEVENTF_LEFTDOWN);
            if ((buttonFlags & Native.RI_MOUSE_LEFT_BUTTON_UP) != 0)
                InjectMouseButton(Native.MOUSEEVENTF_LEFTUP);
            if ((buttonFlags & Native.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
                InjectMouseButton(Native.MOUSEEVENTF_RIGHTDOWN);
            if ((buttonFlags & Native.RI_MOUSE_RIGHT_BUTTON_UP) != 0)
                InjectMouseButton(Native.MOUSEEVENTF_RIGHTUP);
            if ((buttonFlags & Native.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0)
                InjectMouseButton(Native.MOUSEEVENTF_MIDDLEDOWN);
            if ((buttonFlags & Native.RI_MOUSE_MIDDLE_BUTTON_UP) != 0)
                InjectMouseButton(Native.MOUSEEVENTF_MIDDLEUP);

            // Re-inject scroll wheel
            if ((buttonFlags & Native.RI_MOUSE_WHEEL) != 0)
                InjectMouseWheel(Native.MOUSEEVENTF_WHEEL, wheelDelta);
            if ((buttonFlags & Native.RI_MOUSE_HWHEEL) != 0)
                InjectMouseWheel(Native.MOUSEEVENTF_HWHEEL, wheelDelta);
        }

        // --- Secondary mouse: track position + SendMessage for clicks ---

        private bool _isLeftDown = false;
        private bool _isRightDown = false;

        private void HandleSecondaryMouse(int dx, int dy, ushort buttonFlags)
        {
            if (dx != 0 || dy != 0)
            {
                _secondaryCursorX += dx;
                _secondaryCursorY += dy;
                _secondaryCursorX = Math.Clamp(_secondaryCursorX, _screenLeft, _screenRight);
                _secondaryCursorY = Math.Clamp(_secondaryCursorY, _screenTop, _screenBottom);
                SecondaryMouseMoved?.Invoke(_secondaryCursorX, _secondaryCursorY);

                // Send WM_MOUSEMOVE so apps can track drawing while dragging
                int wParam = 0;
                if (_isLeftDown) wParam |= 0x0001; // MK_LBUTTON
                if (_isRightDown) wParam |= 0x0002; // MK_RBUTTON
                PerformSendMessageClick(Native.WM_MOUSEMOVE, wParam);
            }

            // Left click
            if ((buttonFlags & Native.RI_MOUSE_LEFT_BUTTON_DOWN) != 0)
            {
                _isLeftDown = true;
                PerformSendMessageClick(Native.WM_LBUTTONDOWN, 0x0001); // MK_LBUTTON
                SecondaryMouseClicked?.Invoke(true);
            }
            if ((buttonFlags & Native.RI_MOUSE_LEFT_BUTTON_UP) != 0)
            {
                _isLeftDown = false;
                PerformSendMessageClick(Native.WM_LBUTTONUP, 0);
                SecondaryMouseClicked?.Invoke(false);
            }

            // Right click
            if ((buttonFlags & Native.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0)
            {
                _isRightDown = true;
                PerformSendMessageClick(Native.WM_RBUTTONDOWN, 0x0002); // MK_RBUTTON
                SecondaryMouseClicked?.Invoke(true);
            }
            if ((buttonFlags & Native.RI_MOUSE_RIGHT_BUTTON_UP) != 0)
            {
                _isRightDown = false;
                PerformSendMessageClick(Native.WM_RBUTTONUP, 0);
                SecondaryMouseClicked?.Invoke(false);
            }
        }

        private void PerformSendMessageClick(int wmMsg, int wParamValue)
        {
            var pt = new Native.POINT { x = _secondaryCursorX, y = _secondaryCursorY };
            IntPtr targetHwnd = Native.WindowFromPoint(pt);

            if (targetHwnd != IntPtr.Zero)
            {
                Native.ScreenToClient(targetHwnd, ref pt);
                IntPtr lParam = (IntPtr)((pt.y << 16) | (pt.x & 0xFFFF));
                Native.SendMessage(targetHwnd, (uint)wmMsg, (IntPtr)wParamValue, lParam);
            }
        }

        // --- Device enumeration ---
        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        public static List<string> GetConnectedMice()
        {
            var mice = new List<string>();
            uint numDevices = 0;
            int cbSize = Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));

            if (Native.GetRawInputDeviceList(IntPtr.Zero, ref numDevices, (uint)cbSize) == 0)
            {
                IntPtr pList = Marshal.AllocHGlobal((int)(cbSize * numDevices));
                Native.GetRawInputDeviceList(pList, ref numDevices, (uint)cbSize);

                for (int i = 0; i < numDevices; i++)
                {
                    var rid = Marshal.PtrToStructure<RAWINPUTDEVICELIST>(
                        new IntPtr(pList.ToInt64() + (cbSize * i)));
                    if (rid.dwType == Native.RIM_TYPEMOUSE)
                    {
                        mice.Add(rid.hDevice.ToString());
                    }
                }
                Marshal.FreeHGlobal(pList);
            }
            return mice;
        }

        public void Dispose()
        {
            if (_hookID != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }
    }
}

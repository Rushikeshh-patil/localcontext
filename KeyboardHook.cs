using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;

namespace LocalContextBuilder
{
    public class KeyboardHook
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;

        private StringBuilder _contextBuffer = new StringBuilder();
        private System.Timers.Timer _typingTimer;
        private bool _isPaused = false;
        private bool _shiftPressed = false;
        private volatile bool _hasActiveSuggestion = false;

        public bool HasActiveSuggestion
        {
            get => _hasActiveSuggestion;
            set => _hasActiveSuggestion = value;
        }

        public event Action<string>? OnPauseTyping;
        public event Action? OnTyping;
        public event Action? OnAcceptSuggestion;

        public KeyboardHook()
        {
            _proc = HookCallback;
            _typingTimer = new System.Timers.Timer(600); // 600ms pause
            _typingTimer.Elapsed += TypingTimer_Elapsed;
            _typingTimer.AutoReset = false;
        }

        private static void Log(string msg)
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_debug.log"),
                DateTime.Now.ToString("HH:mm:ss.fff") + " [HOOK] " + msg + "\n");
        }

        public void Start()
        {
            _hookID = SetHook(_proc);
            Log($"Hook installed: {_hookID}");
        }

        public void Stop()
        {
            UnhookWindowsHookEx(_hookID);
        }

        public void Pause()
        {
            _isPaused = true;
        }

        public void Resume()
        {
            _isPaused = false;
        }

        private void TypingTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (OnPauseTyping != null) OnPauseTyping(_contextBuffer.ToString());
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !_isPaused)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (wParam == (IntPtr)WM_KEYDOWN)
                {
                    if (vkCode == 0xA0 || vkCode == 0xA1) // Shift
                    {
                        _shiftPressed = true;
                    }
                    else if (vkCode == 0x09) // Tab
                    {
                        Log($"Tab pressed. HasActiveSuggestion={_hasActiveSuggestion}");
                        if (_hasActiveSuggestion)
                        {
                            if (OnAcceptSuggestion != null) OnAcceptSuggestion();
                            return (IntPtr)1; // Block tab only when suggestion is visible
                        }
                        // Otherwise let Tab pass through normally
                    }
                    else if (vkCode == 0x1B) // Escape - dismiss suggestion
                    {
                        if (_hasActiveSuggestion)
                        {
                            if (OnTyping != null) OnTyping();
                        }
                    }
                    else if (vkCode == 0x08) // Backspace
                    {
                        if (_contextBuffer.Length > 0)
                        {
                            _contextBuffer.Length--;
                            ResetTimer();
                        }
                    }
                    else
                    {
                        char c = GetCharFromKey(vkCode, _shiftPressed);
                        if (c != '\0')
                        {
                            _contextBuffer.Append(c);
                            if (_contextBuffer.Length > 200)
                            {
                                _contextBuffer.Remove(0, _contextBuffer.Length - 200);
                            }
                            ResetTimer();
                        }
                    }
                }
                else if (wParam == (IntPtr)WM_KEYUP)
                {
                    if (vkCode == 0xA0 || vkCode == 0xA1)
                    {
                        _shiftPressed = false;
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void ResetTimer()
        {
            if (OnTyping != null) OnTyping();
            _typingTimer.Stop();
            _typingTimer.Start();
        }

        private char GetCharFromKey(int vkCode, bool shift)
        {
            if (vkCode >= 0x41 && vkCode <= 0x5A) // A-Z
            {
                return (char)(shift ? vkCode : vkCode + 32);
            }
            if (vkCode >= 0x30 && vkCode <= 0x39) // 0-9
            {
                return (char)vkCode;
            }
            if (vkCode == 0x20) return ' ';
            if (vkCode == 0x0D) return '\n';
            if (vkCode == 0xBE) return '.';
            if (vkCode == 0xBC) return ',';
            if (vkCode == 0xBF) return '?';
            if (vkCode == 0xBA) return ';';
            if (vkCode == 0xDE) return '\'';
            return '\0';
        }

        public static void InjectText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Log($"InjectText called: '{text}' ({text.Length} chars)");

            INPUT[] inputs = new INPUT[text.Length * 2];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                inputs[i * 2] = new INPUT
                {
                    type = 1, // INPUT_KEYBOARD
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = (ushort)c,
                            dwFlags = 0x0004, // KEYEVENTF_UNICODE
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };
                inputs[i * 2 + 1] = new INPUT
                {
                    type = 1,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = (ushort)c,
                            dwFlags = 0x0004 | 0x0002, // UNICODE | KEYUP
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    }
                };
            }
            int structSize = Marshal.SizeOf(typeof(INPUT));
            Log($"SendInput: {inputs.Length} events, struct size={structSize}");
            uint result = SendInput((uint)inputs.Length, inputs, structSize);
            int err = Marshal.GetLastWin32Error();
            Log($"SendInput result: {result} sent, error={err}");
        }

        // --- P/Invoke Definitions ---
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        // Correct struct layout for x64 compatibility.
        // The union MUST include MOUSEINPUT (the largest member) for proper size calculation.
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }
    }
}

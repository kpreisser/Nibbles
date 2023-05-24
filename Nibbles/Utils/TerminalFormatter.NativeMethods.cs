using System;
using System.Runtime.InteropServices;

namespace Nibbles.Utils;

internal partial class TerminalFormatter
{        
    private class NativeMethods
    {
        public const int StdInputHandle = -10;

        public const int StdOutputHandle = -11;

        public const int StdErrorHandle = -12;

        public const uint EnableVirtualTerminalProcessing = 0x0004;

        public const uint DisableNewlineAutoReturn = 0x0008;
        

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetConsoleMode(
                IntPtr hConsoleHandle,
                out uint lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleMode(
                IntPtr hConsoleHandle,
                uint dwMode);
    }
}

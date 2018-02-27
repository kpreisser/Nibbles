using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Nibbles.Utils
{
    /// <summary>
    /// Formats text on the console by using VT100 terminal sequences.
    /// </summary>
    /// <remarks>
    /// For more information, see:
    /// https://docs.microsoft.com/en-us/windows/console/console-virtual-terminal-sequences
    /// 
    /// For Windows, you must explicitly enable the processing of VT100 sequences by calling
    /// <see cref="EnableWindowsVirtualTerminalSequences"/>
    /// (this only works on Windows 10 Version 1511 an above). On other OSes like Linux,
    /// this works by default.
    /// 
    /// Note that some terminals (e.g. Linux without a GUI) do not support the "Bright"
    /// colors (90-107), and only support bright colors by using the regular color (30-47) and
    /// specifying <see cref="TerminalFormatting.BoldBright"/>. However, this additionally causes
    /// the font to appear bold on some Linux terminals (Linux with GUI).
    /// 
    /// If you don't want the font to appear bold but still want to use a bright color if possible,
    /// you can first specify a dark color (e.g. <see cref="TerminalFormatting.ForegroundRed"/>)
    /// and then the bright color (e.g. <see cref="TerminalFormatting.BrightForegroundRed"/> in
    /// the same call to <see cref="Format(TerminalFormatting[])"/>. Because the last mode that
    /// is supported by the terminal is used, this will use the bright color on terminals that
    /// support it, and the dark color on terminals that don't support it.
    /// </remarks>
    internal partial class TerminalFormatter
    {
        public const int TerminalFormattingBackgroundColorOffset = 10;

        public const int TerminalFormattingBrightColorOffset = 60;

        
        private readonly bool enabled;

        
        public TerminalFormatter(bool enabled = true)
            : base()
        {
            this.enabled = enabled;
        }


        /// <summary>
        /// Enables virtual terminal processing on the StdOut handle for Windows.
        /// </summary>
        /// <exception cref="Win32Exception">If virtual terminal processing is not
        /// supported on this OS version.</exception>
        public static void EnableWindowsVirtualTerminalSequences(bool stdErr = false)
        {
            var handle = NativeMethods.GetStdHandle(
                    stdErr ? NativeMethods.StdErrorHandle : NativeMethods.StdOutputHandle);
            if (handle == (IntPtr)(-1))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (!NativeMethods.GetConsoleMode(handle, out uint consoleMode))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            consoleMode |= NativeMethods.EnableVirtualTerminalProcessing |
                    NativeMethods.DisableNewlineAutoReturn;
            if (!NativeMethods.SetConsoleMode(handle, consoleMode))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        

        /// <summary>
        /// Generates and returns a string that will enable the specified
        /// format when writing it to the console.
        /// </summary>
        /// <param name="formatting">The formats to apply.</param>
        /// <returns></returns>
        public string Format(params TerminalFormatting[] formatting)
        {
            if (!this.enabled || !(formatting?.Length > 0))
                return string.Empty;

            var sb = new StringBuilder("\u001b[");
            for (int i = 0; i < formatting.Length; i++) {
                if (i > 0)
                    sb.Append(';');
                sb.Append(((int)formatting[i]).ToString(CultureInfo.InvariantCulture));
            }
            sb.Append("m");

            return sb.ToString();
        }

        public string SetCursorVisibility(bool show)
        {
            if (!this.enabled)
                return string.Empty;

            return show ? "\u001b[?25h" : "\u001b[?25l";
        }

        public string SwitchAlternateScreenBuffer(bool alternateBuffer)
        {
            if (!this.enabled)
                return string.Empty;

            return "\u001b[?1049" + (alternateBuffer ? "h" : "l");
        }
        
        public string SetCursorPosition(int x, int y)
        {
            if (!this.enabled)
                return string.Empty;

            return $"\u001b[{(y + 1).ToString(CultureInfo.InvariantCulture)};{(x + 1).ToString(CultureInfo.InvariantCulture)}H";
        }
    }
}

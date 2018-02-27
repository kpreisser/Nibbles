using System;
using System.Text;

namespace Nibbles.Utils
{
    internal static class ConsoleUtils
    {
        public static int ConsoleWidth
        {
            get
            {
                try {
                    return Console.WindowWidth;
                }
                catch (Exception) {
                    // This can happen if the process does not have a console associated with it,
                    // e.g. on Windows when the parent process is a GUI application.
                    // In this case we assume 80 as default width.
                    return 80;
                }
            }
        }

        public static int ConsoleHeight
        {
            get
            {
                try {
                    return Console.WindowHeight;
                }
                catch (Exception) {
                    // This can happen if the process does not have a console associated with it,
                    // e.g. on Windows when the parent process is a GUI application.
                    // In this case we assume 25 as default height.
                    return 25;
                }
            }
        }
        
        public static string FixDisplayCharacters(
                string str,
                bool replaceControlCharacters = true)
        {
            if (str == null)
                return null;

            var sb = new StringBuilder(str.Length);

            for (int i = 0; i < str.Length; i++) {
                char c = str[i];
                if (replaceControlCharacters && (/* C0 */ c < 0x20 || c == 0x7F ||
                        /* C1 */ c >= 0x80 && c < 0xA0)) {
                    sb.Append(' ');
                }
                else {
                    switch (c) {
                        case '–':
                            sb.Append('-');
                            break;
                        case '…':
                            // Need to replace '…' because it is not supported on every terminal.
                            sb.Append("...");
                            break;
                        case '‘':
                        case '’':
                            // Some Linux terminals like Raspbian (without GUI) don't support
                            // these typographically correct apostrophes, so use the simple one.
                            sb.Append("'");
                            break;
                        case '®':
                            // Some Linux terminals like Raspbian (without GUI) don't support
                            // this character, so we simply remove it.
                            sb.Append("");
                            break;
                        default:
                            sb.Append(c);
                            break;
                    }
                }
            }

            return sb.ToString();
        }

        public static bool TrySetConsoleTitle(string title)
        {
            try {
                Console.Title = title;
            }
            catch (Exception) {
                return false;
            }

            return true;
        }
    }
}

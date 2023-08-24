using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace Nibbles.Utils;

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

    public static void InitializeConsole()
    {
        // If we are running on Windows, try to enable virtual terminal processing.
        // On other platforms, this should be enabled by default.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                TerminalFormatter.EnableWindowsVirtualTerminalSequences(false);
            }
            catch (Win32Exception)
            {
                // Ignore. This could happen when the streams is redirected, or on
                // Windows 10 Version 1511 and lower.
            }

            try
            {
                TerminalFormatter.EnableWindowsVirtualTerminalSequences(true);
            }
            catch (Win32Exception)
            {
                // Ignore. This could happen when the streams is redirected, or on
                // Windows 10 Version 1511 and lower.
            }
        }

        try
        {
            // Set UTF-8 as output encoding for the console to support Unicode
            // characters.
            // UTF-8 is already the standard on Linux terminals, and on Windows it is
            // supported starting with Windows 10 (Version 1507).
            //
            // Note: On Windows, using UTF-8 means WriteFile is used instead of
            // WriteConsoleW even if the stream is not redirected (whereas the latter
            // would be used with Encoding.Unicode), which means that when starting a
            // child process that changes the console codepage by calling
            // SetConsoleCP/SetConsoleOutputCP (e.g. a command like "chcp 437"), the
            // current console output encoding no longer matches the encoding set in
            // Console.OutputEncoding.
            // Therefore, in such a case you would need to call this method again
            // after the child process has exited.
            Console.OutputEncoding = Encoding.UTF8;

            // Note however, that setting Console.InputEncoding to Encoding.UTF8 will
            // not work on Windows when the standard input stream is not redirected
            // (only ASCII characters would correctly be read), whereas
            // Encoding.Unicode works. Therefore, on Windows we set the InputEncoding
            // to Unicode if the stream is not redirected; otherwise we use UTF-8 to
            // be consistent with Linux and with the output encoding.
            // Note: This might have changed with recent enough Windows 10 Versions
            // (20H2). See:
            // https://github.com/MicrosoftDocs/Console-Docs/blob/5bf6f626dbef5da7944de725819993582ca8fc2d/docs/classic-vs-vt.md#unicode
            if (!OperatingSystem.IsWindows() || Console.IsInputRedirected)
            {
                Console.InputEncoding = Encoding.UTF8;
            }
            else
            {
                Console.InputEncoding = Encoding.Unicode;

                // By default, on Windows the standard input stream will only have a
                // buffer size of 4096 (as of .NET Core 3.0), which means
                // Console.ReadLine() reads only up to 2046 characters on Windows,
                // which can be a problem when entering long license strings. To allow
                // longer strings, we manually need to call OpenStandardInput(),
                // create a StreamReader with a sufficient large buffer size using the
                // Console's InputEncoding, and set Console.In to the new reader.
                // This is the same mechanism that .NET does internally when first
                // accessing Console.In (on Windows).
                // Specifying a higher buffer size in the StreamReader constructor is
                // working because this will determine the number of bytes that are
                // passed to Stream.Read(...), which needs to be sufficently high for
                // the maximum number of characters we want to read.
                // Note that the new StreamReader is not synchronized, so only one
                // thread at a time should read from the StreamReader.
                // Note that this should only be done on Windows (if the stream is not
                // redirected), but not Unix-based OSes, because it seems to interfere
                // with the echo behavior on the console and could lead to
                // Console.ReadLine() no longer echoing after starting a child process.
                const int consoleBufferSize = 65536;
                Console.SetIn(
                        new StreamReader(
                            Console.OpenStandardInput(consoleBufferSize),
                            Console.InputEncoding,
                            detectEncodingFromByteOrderMarks: false,
                            consoleBufferSize,
                            leaveOpen: true));
            }
        }
        catch (IOException)
        {
            // Ignore. Can occur on Windows when no console is attached to the process
            // (e.g. when running as service).
        }
    }


    [return: NotNullIfNotNull("str")]
    public static string? FixDisplayCharacters(
            string str,
            bool replaceControlCharacters = true)
    {
        if (str is null)
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

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

using Nibbles.Utils;

namespace Nibbles;

internal class Program
{
    public static readonly TerminalFormatter Formatter;

    static Program()
    {
        // If we are running on Windows, try to enable virtual terminal processing.
        // On other platforms, this should be enabled by default.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                TerminalFormatter.EnableWindowsVirtualTerminalSequences();
            }
            catch (Exception)
            {
                // Ignore. This could happen when the streams is redirected, or on
                // Windows 10 Version 1511 and lower.
            }

            try
            {
                TerminalFormatter.EnableWindowsVirtualTerminalSequences(stdErr: true);
            }
            catch (Exception)
            {
                // Ignore. This could happen when the streams is redirected, or on
                // Windows 10 Version 1511 and lower.
            }
        }

        Formatter = new TerminalFormatter();

        // Set UTF-8 as output encoding for the console to support Unicode characters.
        // UTF-8 is already the standard on Linux terminals, and on Windows it is supported
        // starting with Windows 10 (Version 1507).
        Console.OutputEncoding = Encoding.UTF8;

        // Note however, that setting Console.InputEncoding to Encoding.UTF8 will not work
        // on Windows when the standard input stream is not redirected,
        // whereas Encoding.Unicode works. Therefore, on Windows we
        // set the InputEncoding to Unicode if the stream is not redirected; otherwise we
        // use UTF-8 to be consistent with Linux and with the output encoding.
        if (OperatingSystem.IsWindows() && !Console.IsInputRedirected)
            Console.InputEncoding = Encoding.Unicode;
        else
            Console.InputEncoding = Encoding.UTF8;

        // Create a new StreamReader with a large buffer so that the user can enter more than
        // 254 characters when calling Console.ReadLine().
        const int consoleBufferSize = 8192;
        Console.SetIn(new StreamReader(Console.OpenStandardInput(consoleBufferSize),
                Console.InputEncoding, false, consoleBufferSize, true));
    }


    static void Main(string[] args)
    {
        // Run the game.
        NibblesGame.RunConsole(Formatter);
    }
}

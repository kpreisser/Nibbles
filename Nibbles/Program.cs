using Nibbles.Utils;

namespace Nibbles;

internal class Program
{
    public static readonly TerminalFormatter Formatter = new();

    static void Main()
    {
        // Initialize the console.
        ConsoleUtils.InitializeConsole();

        // Run the game.
        NibblesGame.RunConsole(Formatter);
    }
}

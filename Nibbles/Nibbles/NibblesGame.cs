using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

using Nibbles.Utils;

namespace Nibbles
{    
    internal class NibblesGame
    {
        private const int fieldWidthOffset = 0;

        private const int fieldHeightOffset = 2;
        

        private readonly WriteConsoleDelegate writeConsoleDelegate;

        private readonly string playerName;

        private readonly int tickWaitTime;

        private readonly TerminalFormatter formatter;

        private readonly StringBuilder consoleBuffer = new StringBuilder();

        private readonly int consoleWidth;

        private readonly int consoleHeight;

        private readonly Random random = new Random();

        private readonly Thread inputThread;

        private readonly ConcurrentQueue<ConsoleKeyInfo> inputQueue = new ConcurrentQueue<ConsoleKeyInfo>();

        private readonly SemaphoreSlim inputQueueSemaphore = new SemaphoreSlim(0);

        private readonly SemaphoreSlim inputQueueWaitSemaphore = new SemaphoreSlim(0);

        private bool inputThreadShouldExit;

        /// <summary>
        /// The screen buffer that is used to draw the field.
        /// </summary>
        private readonly FieldScreenBufferElement[,] fieldScreenBuffer;

        /// <summary>
        /// The current coordinates of the snake from head to tail.
        /// </summary>
        private readonly List<int> currentSnakeCoordinates = new List<int>();

        /// <summary>
        /// The coordinates of the current obstackle. This includes the field border.
        /// </summary>
        private readonly List<int> currentObstacleCoordinates = new List<int>();

        /// <summary>
        /// The current coordinates of the next food.
        /// </summary>
        private int currentFoodCoordinate;

        private int currentFoodLevel;

        private int currentPoints;

        private int remainingLives = 5;

        private SnakeDirection currentDirection;

        private SnakeDirection lastDirection;

        
        private NibblesGame(
                string playerName,
                int speed,
                TerminalFormatter formatter,
                WriteConsoleDelegate writeConsoleDelegate)
            : base()
        {
            if (speed < 0 || speed > 10)
                throw new InvalidOperationException("Invalid speed: " + speed);

            this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            this.writeConsoleDelegate = writeConsoleDelegate ?? throw new ArgumentNullException(nameof(writeConsoleDelegate));
            this.playerName = playerName ?? throw new ArgumentNullException(nameof(playerName));
            this.tickWaitTime = (int)Math.Round(180d * Math.Pow(2, -(speed - 1) * 0.3d));

            // Ensure the console is large enough.
            if (ConsoleUtils.ConsoleWidth < 50 ||
                    ConsoleUtils.ConsoleHeight < 16)
                throw new ArgumentException("The console must have a size of at least 50x16.");

            // Determine the current console size.
            this.consoleWidth = ConsoleUtils.ConsoleWidth;
            this.consoleHeight = ConsoleUtils.ConsoleHeight;

            this.inputThread = new Thread(RunInputThread);

            this.fieldScreenBuffer = new FieldScreenBufferElement[this.FieldWidth, this.FieldHeight];

            //// Create the initial border.

            // First line
            for (int i = 0; i < this.FieldWidth; i++)
                this.currentObstacleCoordinates.Add(i);

            // Middle lines
            for (int i = 1; i < this.FieldHeight - 1; i++) {
                this.currentObstacleCoordinates.Add(i * this.FieldWidth + 0); // Left
                this.currentObstacleCoordinates.Add((i + 1) * this.FieldWidth - 1); // Right
            }

            // Last line
            for (int i = 0; i < this.FieldWidth; i++)
                this.currentObstacleCoordinates.Add((this.FieldHeight - 1) * this.FieldWidth + i);
        }
        

        public static void Run(
            string playerName,
            int speed,
            TerminalFormatter formatter,
            WriteConsoleDelegate writeConsoleDelegate)
        {
            new NibblesGame(playerName, speed, formatter, writeConsoleDelegate).RunCore();
        }

        public static void RunConsole(TerminalFormatter formatter, WriteConsoleDelegate writeConsoleDelegate = null)
        {
            if (formatter == null)
                throw new ArgumentNullException(nameof(formatter));

            if (writeConsoleDelegate == null) {
                writeConsoleDelegate = (str, newLine) => {
                    if (newLine)
                        Console.Out.WriteLine(str);
                    else
                        Console.Out.Write(str);
                };
            }

            ConsoleUtils.TrySetConsoleTitle("Nibbles!");

            // Add a \r to overwrite the title if the terminal doesn't support "ESC ] 0"
            var sb = new StringBuilder("\r");

            const string heading = "N I B B L E S !";
            sb.AppendLine(new string(' ', Math.Max(0, ConsoleUtils.ConsoleWidth / 2 - heading.Length / 2)) + heading);
            sb.AppendLine();
            sb.AppendLine("           Game Controls:");
            sb.AppendLine();
            sb.AppendLine("    General              Player");
            sb.AppendLine("                           (Up)");
            sb.AppendLine("   P - Pause                 ↑");
            sb.AppendLine("   Q - Quit         (Left) ←   → (Right)");
            sb.AppendLine("                             ↓");
            sb.AppendLine("                          (Down)");
            sb.AppendLine();

            sb.Append("Please enter your name: ");
            writeConsoleDelegate(sb.ToString(), false);
            string name = Console.ReadLine();
            writeConsoleDelegate();
            writeConsoleDelegate("Please enter the speed (1-10): ", false);
            try {
                int speed = int.Parse(Console.ReadLine());
                Run(name, speed, formatter, writeConsoleDelegate);
            }
            catch (Exception ex) {
                writeConsoleDelegate();
                writeConsoleDelegate(formatter.Format(TerminalFormatting.ForegroundRed, TerminalFormatting.BoldBright) +
                        "ERROR:" + formatter.Format(TerminalFormatting.None) + " " +
                        ConsoleUtils.FixDisplayCharacters(ex.Message));
            }
        }
        

        private int FieldWidth => this.consoleWidth - fieldWidthOffset;

        private int FieldHeight => this.consoleHeight * 2 - fieldHeightOffset;

        private int FieldYOffset => 1;

        private int SnakeLengthIncreasement => (int)Math.Round(6d / (80d * 25) *
                ((this.FieldWidth * this.FieldHeight - 80d * 25) * 0.7 + 80d * 25));

        
        private void RunCore()
        {
            // Switch to the alternate screen buffer.
            this.consoleBuffer.Append(this.formatter.SwitchAlternateScreenBuffer(true));
            // Hide the cursor.
            this.consoleBuffer.Append(this.formatter.SetCursorVisibility(false));

            // Start the input thread.
            this.inputThread.Start();
            try {
                // Initialize the snake.
                ResetSnakeAndFood();
                // Allow to read a console input key.
                this.inputQueueWaitSemaphore.Release();

                // Draw the initial screen.
                DrawScreen();

                // When the user presses two arrow keys between a tick, we set the first key as
                // new direction and buffer the second key, so that it is applied for the next tick.
                var bufferedDirection = null as SnakeDirection?;

                var sleepStopwatch = new Stopwatch();
                while (true) {
                    bool directionApplied = false;
                    int? remainingWaitTime = null;

                    // Check if we need to apply a buffered direction.
                    if (bufferedDirection != null) {
                        directionApplied = TryApplyDirection(bufferedDirection.Value);
                        bufferedDirection = null;
                    }

                    do {
                        sleepStopwatch.Restart();
                        bool eventAvailable = this.inputQueueSemaphore.Wait(remainingWaitTime ?? this.tickWaitTime);
                        sleepStopwatch.Stop();

                        if (eventAvailable) {
                            if (!this.inputQueue.TryDequeue(out var key))
                                throw new InvalidOperationException(); // should not happen

                            remainingWaitTime = Math.Max(0, (int)((remainingWaitTime ?? this.tickWaitTime) -
                                    sleepStopwatch.ElapsedMilliseconds));

                            // Check what key was pressed.
                            var newDirection = null as SnakeDirection?;
                            switch (key.Key) {
                                case ConsoleKey.UpArrow:
                                    newDirection = SnakeDirection.Up;
                                    break;
                                case ConsoleKey.DownArrow:
                                    newDirection = SnakeDirection.Down;
                                    break;
                                case ConsoleKey.LeftArrow:
                                    newDirection = SnakeDirection.Left;
                                    break;
                                case ConsoleKey.RightArrow:
                                    newDirection = SnakeDirection.Right;
                                    break;
                                case ConsoleKey.P:
                                    HandlePauseKey();
                                    remainingWaitTime = null;
                                    break;
                                case ConsoleKey.Q:
                                    bool shouldQuit = HandleQuitKey();
                                    remainingWaitTime = null;

                                    if (shouldQuit)
                                        return;
                                    break;
                            }

                            // Allow to read the next key.
                            this.inputQueueWaitSemaphore.Release();

                            if (newDirection != null) {
                                if (directionApplied) {
                                    // We have already applied a new direction in this tick,
                                    // so buffer the next one.
                                    bufferedDirection = newDirection;
                                }
                                else {
                                    // Apply the direction directly.
                                    directionApplied = TryApplyDirection(newDirection.Value);
                                }
                            }
                        }
                        else {
                            remainingWaitTime = null;
                        }
                    }
                    while (remainingWaitTime > 0);

                    bool result = MoveSnake();
                    if (!result) {
                        this.remainingLives--;
                        this.currentPoints -= 1000;
                        // Refresh the title with the remaining lives.
                        DrawTitle();

                        if (this.remainingLives > 0)
                            DisplayMessage("Snake dies! Press Space to continue.");
                        else
                            DisplayMessage("Game over! Press Space to exit.");

                        // Wait until space was pressed.
                        while (true) {
                            this.inputQueueSemaphore.Wait();
                            if (!this.inputQueue.TryDequeue(out var pausingKey))
                                throw new InvalidOperationException(); // should not happen
                            if (pausingKey.Key == ConsoleKey.Spacebar)
                                break;

                            this.inputQueueWaitSemaphore.Release();
                        }

                        // If there are no lives remaining, we need to exit.
                        if (this.remainingLives == 0)
                            break;

                        // Otherwise, start again with a new snake.
                        ResetSnakeAndFood();

                        this.inputQueueWaitSemaphore.Release();

                        // Redraw the field.
                        DrawField();
                        FlushConsoleBuffer();
                    }
                }
            }
            finally {
                // Restore the console settings.
                this.consoleBuffer.Append(this.formatter.SwitchAlternateScreenBuffer(false));
                this.consoleBuffer.Append(this.formatter.SetCursorVisibility(true));
                this.consoleBuffer.Append(this.formatter.Format(0));
                FlushConsoleBuffer();

                // Exit the input thread.
                Volatile.Write(ref this.inputThreadShouldExit, true);
                this.inputQueueWaitSemaphore.Release();
                this.inputThread.Join();
                this.inputQueueSemaphore.Dispose();
                this.inputQueueWaitSemaphore.Dispose();
            }
        }

        private void HandlePauseKey()
        {
            DisplayMessage("Game Paused... Press Space to continue");

            // Wait until space was pressed.
            while (true) {
                this.inputQueueWaitSemaphore.Release();
                this.inputQueueSemaphore.Wait();
                if (!this.inputQueue.TryDequeue(out var pausingKey))
                    throw new InvalidOperationException(); // should not happen
                if (pausingKey.Key == ConsoleKey.Spacebar)
                    break;
            }

            // Redraw the screen.
            DrawScreen();
        }

        private bool HandleQuitKey()
        {
            DisplayMessage("Do you really want to quit?  (Y/N)");

            // Wait until Y or N was pressed.
            try {
                while (true) {
                    this.inputQueueWaitSemaphore.Release();
                    this.inputQueueSemaphore.Wait();
                    if (!this.inputQueue.TryDequeue(out var pausingKey))
                        throw new InvalidOperationException(); // should not happen
                    if (pausingKey.Key == ConsoleKey.N)
                        return false;
                    else if (pausingKey.Key == ConsoleKey.Y)
                        return true;
                }
            }
            finally {
                // Redraw the screen.
                DrawScreen();
            }
        }

        private bool TryApplyDirection(SnakeDirection newDirection)
        {
            bool directionApplied = false;

            if (newDirection == SnakeDirection.Up && this.currentDirection != SnakeDirection.Down ||
                    newDirection == SnakeDirection.Down && this.currentDirection != SnakeDirection.Up ||
                    newDirection == SnakeDirection.Left && this.currentDirection != SnakeDirection.Right ||
                    newDirection == SnakeDirection.Right && this.currentDirection != SnakeDirection.Left) {
                directionApplied = true;
                this.currentDirection = newDirection;
            }

            return directionApplied;
        }

        private void ResetSnakeAndFood()
        {
            this.currentFoodLevel = 0;
            this.currentSnakeCoordinates.Clear();
            this.currentSnakeCoordinates.Add(this.FieldHeight / 2 * this.FieldWidth + this.FieldWidth / 2);
            this.currentSnakeCoordinates.Add((this.FieldHeight / 2 + 1) * this.FieldWidth + this.FieldWidth / 2);
            this.currentDirection = SnakeDirection.Up;
            this.lastDirection = this.currentDirection;

            // Set the next food.
            SetNextFood();
        }

        private void SetNextFood()
        {
            // Get the currently occupied coordinates and sort them.
            var occupiedFields = new int[this.currentSnakeCoordinates.Count + this.currentObstacleCoordinates.Count];
            for (int i = 0; i < this.currentSnakeCoordinates.Count; i++)
                occupiedFields[i] = this.currentSnakeCoordinates[i];
            for (int i = 0; i < this.currentObstacleCoordinates.Count; i++)
                occupiedFields[this.currentSnakeCoordinates.Count + i] = this.currentObstacleCoordinates[i];
            Array.Sort(occupiedFields);

            // Place the next food at a random free position. To do this, we first get a
            // random number between 0 and the max player field minus the occupied
            // coordinates, and then adjust the number accordingly.
            int number = this.random.Next(this.FieldWidth * this.FieldHeight -
                    occupiedFields.Length);

            for (int i = 0; i < occupiedFields.Length; i++) {
                if (occupiedFields[i] <= number)
                    number++;
                else
                    break;
            }

            this.currentFoodCoordinate = number;
        }

        private bool MoveSnake()
        {
            var coordinatesToRedraw = new List<int>();

            // Move the coordinates.
            bool snakeGotLonger = false;

            int lastCoordinate = this.currentSnakeCoordinates[this.currentSnakeCoordinates.Count - 1];
            coordinatesToRedraw.Add(lastCoordinate);

            for (int i = this.currentSnakeCoordinates.Count - 1; i >= 1; i--)
                this.currentSnakeCoordinates[i] = this.currentSnakeCoordinates[i - 1];

            int expectedSnakeLength = this.currentFoodLevel * this.SnakeLengthIncreasement + 2;
            if (this.currentSnakeCoordinates.Count < expectedSnakeLength) {
                // Re-add the last coordinate.
                this.currentSnakeCoordinates.Add(lastCoordinate);
                snakeGotLonger = true;
            }

            int x = this.currentSnakeCoordinates[0] % this.FieldWidth;
            int y = this.currentSnakeCoordinates[0] / this.FieldWidth;

            switch (this.currentDirection) {
                case SnakeDirection.Up:
                    y--;
                    break;
                case SnakeDirection.Down:
                    y++;
                    break;
                case SnakeDirection.Left:
                    x--;
                    break;
                case SnakeDirection.Right:
                    x++;
                    break;
            }

            this.lastDirection = this.currentDirection;

            this.currentSnakeCoordinates[0] = y * this.FieldWidth + x;
            coordinatesToRedraw.Add(this.currentSnakeCoordinates[0]);

            // Check if the snake's head collided with something.
            if (x < 0 || y < 0 || x >= this.FieldWidth || y >= this.FieldHeight)
                return false; // This should not happen, as we create an border as obstacle.

            // Check if the snake collided with itself.
            for (int i = 1; i < this.currentSnakeCoordinates.Count; i++)
                if (this.currentSnakeCoordinates[i] == this.currentSnakeCoordinates[0])
                    return false;

            // Check if the snake collided with the obstacle.
            for (int i = 0; i < this.currentObstacleCoordinates.Count; i++)
                if (this.currentObstacleCoordinates[i] == this.currentSnakeCoordinates[0])
                    return false;

            if (this.currentSnakeCoordinates[0] == this.currentFoodCoordinate) {
                // We ate the food, so generate a new one, and make the snake larger
                // (only if it didn't get longer already).
                this.currentFoodLevel++;
                this.currentPoints += this.currentFoodLevel * 100;
                if (!snakeGotLonger)
                    this.currentSnakeCoordinates.Add(lastCoordinate);

                DrawTitle();

                // Set the next food coordinate after the snake got longer.
                SetNextFood();
                coordinatesToRedraw.Add(this.currentFoodCoordinate);
            }

            // Redraw the field with the specified coordinates.
            DrawField(coordinatesToRedraw);
            FlushConsoleBuffer();

            // Everything OK.
            return true;
        }

        private void RunInputThread()
        {
            try {
                while (true) {
                    // Wait until we may process the next key.
                    this.inputQueueWaitSemaphore.Wait();
                    // Check if we need to exit.
                    if (Volatile.Read(ref this.inputThreadShouldExit))
                        break;

                    // Read the next key and add it to the queue.
                    var key = Console.ReadKey(true);
                    this.inputQueue.Enqueue(key);
                    this.inputQueueSemaphore.Release();
                }
            }
            catch {
                // Could happen if the process does not have a console or the input stream is redirected.
                // Ignore.
            }
        }

        private void FlushConsoleBuffer()
        {
            this.writeConsoleDelegate(this.consoleBuffer.ToString(), false);
            this.consoleBuffer.Clear();
        }

        private void DrawScreen()
        {
            DrawTitle();
            DrawField();

            FlushConsoleBuffer();
        }

        private void DrawTitle()
        {
            const string prefix = " Nibbles!   Player: ";
            string playerNameDisplayString = ConsoleUtils.FixDisplayCharacters(this.playerName);
            string suffix = $"   Lives: {this.remainingLives}   Points: {this.currentPoints} ";

            if (prefix.Length + playerNameDisplayString.Length + suffix.Length > this.consoleWidth)
                playerNameDisplayString = playerNameDisplayString.Substring(0,
                        Math.Max(0, this.consoleWidth - prefix.Length - suffix.Length - 3)) + "...";
            else
                playerNameDisplayString += new string(' ', this.consoleWidth -
                        (prefix.Length + playerNameDisplayString.Length + suffix.Length));

            this.consoleBuffer.Append(this.formatter.SetCursorPosition(0, 0)).Append(prefix)
                    .Append(playerNameDisplayString).Append(suffix);
        }

        private void DrawField(IReadOnlyList<int> drawOnlyCoordinates = null)
        {
            // Ensure the console size has not changed.
            if (ConsoleUtils.ConsoleWidth != this.consoleWidth || ConsoleUtils.ConsoleHeight != this.consoleHeight)
                throw new InvalidOperationException("Console size has changed during runtime.");

            // First, prepare the buffer where we set the fields to draw. Then, we will draw two
            // lines of the buffer to one line on the screen.
            Array.Clear(this.fieldScreenBuffer, 0, this.fieldScreenBuffer.Length);

            foreach (var snakeCoordinate in this.currentSnakeCoordinates) {
                this.fieldScreenBuffer[snakeCoordinate % this.FieldWidth, snakeCoordinate / this.FieldWidth] =
                        FieldScreenBufferElement.Snake;
            }
            foreach (var obstacleCoordinate in this.currentObstacleCoordinates) {
                this.fieldScreenBuffer[obstacleCoordinate % this.FieldWidth, obstacleCoordinate / this.FieldWidth] =
                        FieldScreenBufferElement.Obstacle;
            }

            this.fieldScreenBuffer[this.currentFoodCoordinate % this.FieldWidth, this.currentFoodCoordinate / this.FieldWidth] =
                    FieldScreenBufferElement.Food;

            // Now, draw the field onto the screen.
            // For elements that are 'None', we use the background color, otherwise we use the red
            // or yellow foreground color. If there are two different elements other than 'None'
            // mapping to the same screen line, we use the foreground color for one element and the
            // background color for the other. Note that this can lead to one of the elements
            // being drawn in a darker color if the terminal doesn't support bright colors.
            const char noneChar = ' ', fullChar = '█', upperHalfChar = '▀', lowerHalfChar = '▄';

            const TerminalFormatting blue = TerminalFormatting.ForegroundBlue;
            const TerminalFormatting yellow = TerminalFormatting.ForegroundYellow;
            const TerminalFormatting red = TerminalFormatting.ForegroundRed;
            const TerminalFormatting white = TerminalFormatting.ForegroundWhite;

            var currentForeground = null as TerminalFormatting?;
            var currentBackground = null as TerminalFormatting?;
            var currentIsBrightBackground = null as bool?;

            TerminalFormatting getElementColor(FieldScreenBufferElement element)
            {
                switch (element) {
                    case FieldScreenBufferElement.Snake:
                        return yellow;
                    case FieldScreenBufferElement.Obstacle:
                        return red;
                    case FieldScreenBufferElement.Food:
                        return white;
                    default:
                        throw new ArgumentException();
                }
            }

            void setColor(TerminalFormatting foreground, TerminalFormatting background, bool isBrightBackground = false)
            {
                if (!(foreground == currentForeground && background == currentBackground &&
                        isBrightBackground == currentIsBrightBackground)) {
                    // Set the bold/bright flag so that the terminal uses the bright color.
                    var formattings = new List<TerminalFormatting>()
                    {
                        // To ensure the foreground color is bright, we additionally specify the
                        // dark color + bold/bright mode as fallback. However the explicit bright color
                        // is also needed for Terminals that have black text on white background, e.g.
                        // on macOS.
                        TerminalFormatting.BoldBright,
                        foreground,
                        foreground + TerminalFormatter.TerminalFormattingBrightColorOffset,
                        background + TerminalFormatter.TerminalFormattingBackgroundColorOffset
                    };

                    // When we use a bright background, we use the darker color as fallback for
                    // terminals that don't support a bright background color.
                    if (isBrightBackground)
                        formattings.Add(background +
                                TerminalFormatter.TerminalFormattingBackgroundColorOffset +
                                TerminalFormatter.TerminalFormattingBrightColorOffset);

                    this.consoleBuffer.Append(this.formatter.Format(formattings.ToArray()));

                    currentForeground = foreground;
                    currentBackground = background;
                    currentIsBrightBackground = isBrightBackground;
                }
            }

            void drawCoordinate(int x, int y)
            {
                // y here is the line number, for which we draw two "sublines".
                var upperLine = this.fieldScreenBuffer[x, y * 2];
                var lowerLine = this.fieldScreenBuffer[x, y * 2 + 1];

                if (upperLine == lowerLine && upperLine == FieldScreenBufferElement.None) {
                    // If both lines have no elements, we simply draw the space char with
                    // blue background.
                    setColor(red, blue);
                    this.consoleBuffer.Append(noneChar);
                }
                else if (upperLine == lowerLine && upperLine != FieldScreenBufferElement.None) {
                    // Both lines have the same element. Note that while we use the 'fullChar' for
                    // this, we still set the background color to be the same as the foreground color,
                    // to compensate for some terminals that do not draw the block characters for the
                    // full line height, e.g. like in the terminal on macOS.
                    var color = getElementColor(upperLine);
                    setColor(color, color, true);
                    this.consoleBuffer.Append(fullChar);
                }
                else if (upperLine == FieldScreenBufferElement.None || lowerLine == FieldScreenBufferElement.None) {
                    // Only one line has an element.
                    char charToDraw = upperLine == FieldScreenBufferElement.None ? lowerHalfChar : upperHalfChar;
                    var color = getElementColor(upperLine == FieldScreenBufferElement.None ? lowerLine : upperLine);
                    setColor(color, blue);
                    this.consoleBuffer.Append(charToDraw);
                }
                else {
                    // Both lines have (different) elements. In this case we user the upper line
                    // as foreground and the lower as (bright) background.
                    var upperColor = getElementColor(upperLine);
                    var lowerColor = getElementColor(lowerLine);
                    setColor(upperColor, lowerColor, true);
                    this.consoleBuffer.Append(upperHalfChar);
                }
            }

            if (drawOnlyCoordinates == null) {
                // Draw the whole field.
                for (int y = 0; y < this.FieldHeight / 2; y++) {
                    // Move the cursor to the line.
                    this.consoleBuffer.Append(this.formatter.SetCursorPosition(0, y + this.FieldYOffset));

                    for (int x = 0; x < this.FieldWidth; x++) {
                        drawCoordinate(x, y);
                    }
                }
            }
            else {
                // Draw only the coordinates that were specified.
                // Note that we currently don't detect duplicate coordinates.
                foreach (var coordinate in drawOnlyCoordinates) {
                    int y = coordinate / this.FieldWidth / 2;
                    int x = coordinate % this.FieldWidth;

                    // Move the cursor to the point.
                    this.consoleBuffer.Append(this.formatter.SetCursorPosition(x, y + this.FieldYOffset));
                    drawCoordinate(x, y);
                }
            }

            this.consoleBuffer.Append(this.formatter.Format(0));
        }

        private void DisplayMessage(string s)
        {
            var lines = s.Replace("\r", "").Split('\n');
            int drawLineLength = lines.Max(l => l.Length) + 4;

            string formatting = this.formatter.Format(TerminalFormatting.ForegroundWhite,
                    TerminalFormatting.BackgroundBlack);
            string formattingFull = this.formatter.Format(TerminalFormatting.ForegroundWhite,
                    TerminalFormatting.BackgroundWhite);
            string formatNone = this.formatter.Format(0);

            var drawLines = new List<string>(lines.Length + 2);
            drawLines.Add(formattingFull + '█' + formatNone + formatting +
                    new string('▀', drawLineLength - 2) + formattingFull + '█' + formatNone);
            foreach (var line in lines) {
                int fillLeft = (drawLineLength - 2 - line.Length) / 2;
                drawLines.Add(formattingFull + '█' + formatNone + formatting +
                        new string(' ', fillLeft) + this.formatter.Format(TerminalFormatting.BoldBright) +
                        line + formatNone + formatting +
                        new string(' ', drawLineLength - 2 - line.Length - fillLeft) +
                        formattingFull + '█' + formatNone);
            }
            drawLines.Add(formattingFull + '█' + formatNone + formatting +
                    new string('▄', drawLineLength - 2) + formattingFull + '█' + formatNone);

            int topY = this.consoleHeight / 2 - drawLines.Count / 2;
            int topX = this.consoleWidth / 2 - drawLineLength / 2;
            for (int i = 0; i < drawLines.Count; i++) {
                this.consoleBuffer.Append(this.formatter.SetCursorPosition(topX, topY + i))
                        .Append(drawLines[i]);
            }
            FlushConsoleBuffer();
        }
    }
}

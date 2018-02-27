# Nibbles

A simple, console-based snake game, implemented in C# and running on .NET Core, inspired by
[QBasic Nibbles](https://en.wikipedia.org/wiki/Nibbles_(video_game)) from 1990.

Because it is built for .NET Core, it can run on any platform that .NET Core supports, e.g.
Windows 10 Anniversary Update (or higher), Linux and macOS.

As a console application, it uses
[Virtual Terminal Sequences](https://docs.microsoft.com/en-us/windows/console/console-virtual-terminal-sequences)
that are supported on Linux/macOS terminals, and also on Windows 10 Anniversary Update (Version 1607) or higher,
to control colors, cursor movement/visibility and the screen buffer of the console.

## Screenshots

Windows 10:

![nibbles-windows](https://user-images.githubusercontent.com/13289184/36750845-d939e38c-1bfe-11e8-82a6-b8762fac28c3.png)

Ubuntu (with Desktop Environment):

![nibbles-ubuntu](https://user-images.githubusercontent.com/13289184/36749736-f1778916-1bfb-11e8-9ca0-0b05530cf036.png)

CentOS (without Desktop Environment):

![nibbles-centos](https://user-images.githubusercontent.com/13289184/36749753-fc4465b2-1bfb-11e8-964a-67be6ef4a364.png)

macOS (unfortunately, macOS's Terminal.app doesn't draw block characters at full line height, which is
why it doesn't look as good as on other OSes):

![nibbles-macos](https://user-images.githubusercontent.com/13289184/36751581-e5f45e0c-1c00-11e8-99dd-94cc84d75741.png)

## Running

Install the [.NET Core SDK](https://www.microsoft.com/net/download/windows) 2.1.4 or higher. Then run:
```
git clone https://github.com/kpreisser/Nibbles.git
cd Nibbles/Nibbles
dotnet run
```
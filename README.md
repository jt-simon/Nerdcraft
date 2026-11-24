# Nerdcraft

[![CI](https://github.com/jt-simon/Nerdcraft/actions/workflows/ci.yml/badge.svg)](https://github.com/jt-simon/Nerdcraft/actions/workflows/ci.yml)

A voxel sandbox game written in C# on .NET 10.

**Status:** early prototype, still in the planning stage. Terrain generates and streams around a fly
camera; not much else exists yet, and everything is likely to change.

## Run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet run --project src/Nerdcraft.Desktop
```

WASD move, Space jump (swim up, fly up), Shift sneak (swim down, fly down), double-tap W sprint, F toggle flight, N toggle noclip, mouse look, left button break, right button place, 1-9 / wheel hotbar, F3 debug overlay, Esc pause menu (Resume / Quit, frees the mouse), F11 maximize.

Your world is saved automatically (every few seconds and on quit) in `~/.local/share/Nerdcraft/worlds/default` on Linux
(`%LOCALAPPDATA%\Nerdcraft\worlds\default` on Windows). Delete that folder to start a new world.

## License

GNU Affero General Public License v3.0 or later (AGPL-3.0-or-later). See [LICENSE](LICENSE).

## More

- [How it's built](docs/architecture.md)
- [Contributing](CONTRIBUTING.md)
- [Rules for contributors and coding agents](AGENTS.md)

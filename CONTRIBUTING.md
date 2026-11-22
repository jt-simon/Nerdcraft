# Contributing

Thanks for helping with Nerdcraft. The project is an early prototype, so please open an issue to discuss
anything large before you build it.

## Build and test

Requires the .NET 10 SDK.

```bash
dotnet build Nerdcraft.slnx                        # zero warnings (warnings are errors)
dotnet test --solution Nerdcraft.slnx
dotnet format --verify-no-changes Nerdcraft.slnx   # run `dotnet format whitespace Nerdcraft.slnx` to fix
```

CI runs the same three commands on every pull request.

## Rules

[AGENTS.md](AGENTS.md) holds the project's rules: strict clean architecture, a lean test suite, XML docs on
public API, and a short workflow. They apply to people and coding agents alike, so please read it first.

## Pull requests

- Keep them small and focused, and don't refactor or reformat code outside the change.
- CI must be green. Don't loosen a test, suppress a warning or edit `.editorconfig` to get there.
- Add tests for behavior that can actually break, not for getters or constants.
- New `.cs` files start with `// SPDX-License-Identifier: AGPL-3.0-or-later` and a blank line. The build checks it.

## Versioning and changelog

Nerdcraft uses [Semantic Versioning](https://semver.org/). The version is `<Version>` in
`Directory.Build.props`. While it is 0.x, a minor bump may break things and a patch bump is for fixes.

Add an entry under `[Unreleased]` in [CHANGELOG.md](CHANGELOG.md) for any user-visible change, grouped as
Added, Changed, Fixed or Removed. Skip internal refactors, test-only changes and dependency bumps unless they
change behavior.

To release, a maintainer moves `[Unreleased]` into a dated version section, bumps `<Version>`, updates the
compare links, merges, then tags the merge commit with `git tag vX.Y.Z && git push --tags`.

## License

By contributing you agree that your work is licensed under the
[GNU Affero General Public License v3.0 or later](LICENSE).

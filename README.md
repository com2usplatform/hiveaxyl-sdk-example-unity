# Hive Axyl SDK for Unity: Recipes and Examples

> **Official repository** of Com2uS Platform.
> Canonical URL: <https://github.com/com2usplatform/hiveaxyl-sdk-example-unity>

| | |
| --- | --- |
| Product | Hive Axyl |
| Domain | sdk |
| Version | see [`CHANGELOG.md`](CHANGELOG.md) |
| Lifecycle | Active |
| Related | [hiveaxyl-sdk-unity](https://github.com/com2usplatform/hiveaxyl-sdk-unity): the SDK these Recipes call |

Reference code for calling the Hive Axyl SDK for Unity in the right order. The SDK exposes each
platform feature as an individual call and leaves the sequence to the game. A Recipe is one worked
answer to "what do I call, and in what order" for sign-in, purchases, push and local notifications,
and the example beside it shows the steps the game supplies.

| Folder | Contents |
| --- | --- |
| [`Assets/RecipeExamples/`](Assets/RecipeExamples/README.md) | One short, UI-free example per Recipe, with each decision left to the game marked `// APP:`. Start here. |
| [`Assets/Recipes/`](Assets/Recipes/README.md) | The Recipes: UI-free code that coordinates SDK calls and returns typed outcomes. |

Recipes are reference code to copy and adapt, not a library to depend on. There is no API
compatibility promise: a Recipe is correct for the SDK release it was verified against, which
[`CHANGELOG.md`](CHANGELOG.md) records, and may change with the next one.

This repository carries no runnable scene. It is a Unity project so that the Recipes compile
against the SDK packages pinned in `Packages/manifest.json`.

## Requirements

The project is saved with Unity 6000.3.21f1. To add the SDK to your own game, follow the
installation steps in [hiveaxyl-sdk-unity](https://github.com/com2usplatform/hiveaxyl-sdk-unity).

## Using a Recipe in your game

Copy the Recipes you need as [`Assets/Recipes/README.md`](Assets/Recipes/README.md#copying-these-into-a-game)
describes: the shared files every Recipe compiles with, and the dependencies each folder forces.

## Support

GitHub Issues are not used as a support channel for this repository. Please use the channels below
for questions, bug reports, feature requests, and customer inquiries.

| Purpose | Channel |
| --- | --- |
| Questions, bug reports, feature requests | <cs-platform@com2us.com> |
| Security vulnerabilities | Private report only, see [`SECURITY.md`](SECURITY.md) |

GitHub is not an official customer support channel, and we do not commit to response or resolution
timelines here. External pull requests are not accepted.

## License

Licensed under the Apache License, Version 2.0. See [`LICENSE`](LICENSE) and [`NOTICE`](NOTICE).

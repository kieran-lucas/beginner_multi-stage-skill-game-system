# Anime Fighter Prototype

Unity 2.5D anime fighting prototype: Void Sorcerer vs Crimson Curse.

## Play The Windows Build

Double-click:

```text
Play_Game.bat
```

The launcher starts:

```text
Builds/Windows/AnimeFighterPrototype.exe
```

You can also run the game directly by double-clicking:

```text
Builds/Windows/AnimeFighterPrototype.exe
```

Keep the generated build data folder beside the executable. Do not move only
the `.exe`.

## Run From Unity Editor

1. Open the project in Unity.
2. Open `Assets/_Project/Scenes/PrototypeArena.unity`.
3. Press Play.

The project version recorded in `ProjectSettings/ProjectVersion.txt` is Unity
`6000.0.30f1`.

## Controls

| Action | Input |
| --- | --- |
| Move | `A` / `D` or arrow keys |
| Jump | `Space` |
| Dash | `Left Shift` |
| Block | `I` or right mouse |
| Basic attack | `J` |
| State Skill inputs | `Q`, `W`, `E` |

Debug hotkeys are enabled on `GameManager` for prototype testing:

| Key | Action |
| --- | --- |
| `F1` | Add Cursed Energy |
| `F2` | Damage player |
| `F3` | Damage enemy |
| `F4` | Reset State Skill |
| `F5` | Force enemy death |
| `F6` | Reset camera/time scale |

## State Skill Rules

State Skills do not activate from one key. Enter each sequence in order within
the 1.5 second window:

| Transition | Sequence | Energy |
| --- | --- | ---: |
| Normal to Activation | `Q -> W -> E` | 10 |
| Activation to Charge | `W -> E -> Q` | 20 |
| Charge to Release | `E -> Q -> W` | 35 |

Wrong input resets only the current sequence attempt, applies a small energy
penalty, briefly delays input, and triggers UI/VFX feedback. Release deals
large Ultimate damage, applies knockback, then enters cooldown before returning
to Normal.

## Automated Build

The build entry point is:

```text
AnimeFighter.Editor.BuildScript.BuildWindows
```

It builds:

```text
Builds/Windows/AnimeFighterPrototype.exe
```

using:

```text
Assets/_Project/Scenes/PrototypeArena.unity
```

## Known Limitations

- Characters, VFX, UI, and camera effects are prototype placeholders.
- Enemy AI is medium-difficulty and probabilistic, not competition-tuned.
- No authored animation controller, audio pass, Cinemachine setup, or post-processing stack is included.
- The project uses the legacy Input Manager.

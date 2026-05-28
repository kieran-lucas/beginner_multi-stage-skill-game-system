# beginner_multi-stage-skill-game-system

A Unity 2.5D anime-fighting prototype demonstrating a multi-stage skill
execution system, where abilities are performed through sequential player
inputs (Q / W / E timed chains) instead of single-button activation.

Placeholder characters:

- **Void Sorcerer** — player (blue/purple)
- **Crimson Curse** — enemy (red/black)

These are inspiration-only stand-ins. Production builds must not ship under
copyrighted names or designs.

## Current state — foundation only

This commit sets up:

- `Assets/_Project/` folder structure with Scripts, Materials, Scenes,
  Prefabs, ScriptableObjects, Art, Audio.
- A `PrototypeArena.unity` scene with side-view camera, light, ground,
  background, player + enemy with child anchors (`VisualRoot`, `HitboxRoot`,
  `VFXRoot`, `UIAnchor`, `GroundCheck`, `AttackOrigin`, `SkillOrigin`), and
  a `Managers` GameObject.
- Manager scripts: `GameManager`, `CameraEffects`, `VFXManager`, `UIManager`.
- Placeholder scripts: `PlayerController`, `EnemyAI`, `HealthSystem`,
  `EnergySystem`, `CombatController`, `InputSequenceDetector`,
  `StateSkillController`.

No combat, AI, or skill logic is implemented yet — that comes in later
prompts. See [SETUP.md](SETUP.md) for the scene map and the extension hooks
future prompts should target.

## Running

1. Open the project in Unity Hub (**Unity 6.0 LTS**, `6000.0.30f1`).
2. Open `Assets/_Project/Scenes/PrototypeArena.unity`.
3. Press Play — `GameManager` will transition from `Boot` to `Playing`.

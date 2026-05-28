# Setup notes — PrototypeArena foundation

This document describes the scene that ships with this foundation prompt and
the hooks that later prompts should target. It is intentionally short.

## Required Unity version

- **Unity 6.0 LTS** (`6000.0.30f1`) — recorded in `ProjectSettings/ProjectVersion.txt`.
- Built-in 3D render pipeline. URP and HDRP have not been configured. If you
  swap to URP later, the four placeholder materials will need their shader
  switched to `Universal Render Pipeline/Lit` to avoid pink shaders.

## Opening the project

1. Open Unity Hub → **Add** → select this repository root.
2. Unity will import and create the missing `ProjectSettings/*.asset` files
   from defaults on first open.
3. Open `Assets/_Project/Scenes/PrototypeArena.unity`.
4. Press Play. The console should be clean and `GameManager` should
   transition from `Boot` to `Playing` on `Start()`.

## Folder layout

```
Assets/_Project/
├── Art/Placeholder/          # textures, sprites, model placeholders
├── Audio/Placeholder/        # SFX / VO scratch
├── Materials/                # VoidSorcerer, CrimsonCurse, Ground, Background
├── Prefabs/                  # combat hit-VFX prefabs, projectile prefabs, etc.
├── Scenes/
│   └── PrototypeArena.unity  # the only scene for this prototype
├── ScriptableObjects/        # state-skill definitions land here
└── Scripts/
    ├── Core/      GameManager
    ├── Player/    PlayerController
    ├── Enemy/     EnemyAI
    ├── Combat/    HealthSystem, EnergySystem, CombatController
    ├── Skills/    InputSequenceDetector, StateSkillController
    ├── UI/        UIManager
    └── VFX/       CameraEffects, VFXManager
```

Namespaces match folders: `AnimeFighter.Core`, `AnimeFighter.Player`,
`AnimeFighter.Enemy`, `AnimeFighter.Combat`, `AnimeFighter.Skills`,
`AnimeFighter.UI`, `AnimeFighter.VFX`.

## Scene hierarchy

```
PrototypeArena
├── Main Camera                  (0, 2.5, -10)  FOV 38, side-view framing
├── Directional Light            key light, no shadows tuned yet
├── Arena
│   ├── Ground                   Plane,  Ground.mat   (40 × 20 units)
│   └── BackgroundDecor          Quad,   Background.mat at z = +6
├── Void Sorcerer                (-3, 1, 0)  facing +X
│   ├── VisualRoot               Capsule mesh + VoidSorcerer.mat
│   ├── HitboxRoot               empty — hitbox prefabs spawn here
│   ├── VFXRoot                  empty — character VFX spawn here
│   ├── UIAnchor                 above head, for world-space HUD
│   ├── GroundCheck              at feet, for grounded raycasts
│   ├── AttackOrigin             in front of body, basic-attack hitboxes
│   └── SkillOrigin              forward + up, projectile / skill spawns
│   Components: Rigidbody, CapsuleCollider, PlayerController, HealthSystem,
│               EnergySystem, CombatController, InputSequenceDetector,
│               StateSkillController
├── Crimson Curse                ( 3, 1, 0)  facing -X
│   └── (mirrors the player; EnemyAI + HealthSystem + CombatController)
└── Managers                     GameManager, CameraEffects, VFXManager, UIManager
```

Rigidbody constraints on both characters: `FreezeRotation X/Y/Z + FreezePosition Z`
(value `116`) — keeps them upright and pinned to the gameplay plane.

## How future prompts should plug in

| System                        | Where it goes                                                       |
| ----------------------------- | ------------------------------------------------------------------- |
| Player movement / jump / dash | **Implemented.** `PlayerController` — see _Player controls_ below   |
| Basic Light attack + combo    | **Implemented.** `CombatController` — Heavy/Skill/Ultimate reuse the same pipeline via `AttackType` |
| Damage application            | **Implemented.** `HealthSystem` (`IDamageable`) — events: `OnHealthChanged`, `OnDamaged`, `OnDeath` |
| Energy gain / spend           | **Implemented.** `EnergySystem` — `SpendEnergy` / `AddEnergy` / `NotifyHitLanded` / `NotifyBlocked` |
| Dash energy cost              | Subscribe `EnergySystem.SpendEnergy` to `PlayerController.DashRequested` |
| Q/W/E sequence detection      | **Implemented.** `InputSequenceDetector` — timed ordered chains with progress, failure, completion, and timeout events |
| Multi-stage skill execution   | **Implemented.** `StateSkillController` — drives Activation, Charge, Release, cooldown, energy spend, and ultimate damage |
| Skill data (cost, stages)     | `Assets/_Project/ScriptableObjects/` — `CreateAssetMenu` SOs       |
| HUD bars + stage indicator    | **Implemented.** `UIManager` builds a runtime HUD and subscribes to health, energy, combo, State Skill, and sequence events |
| Camera shake / hit-pause      | **Implemented.** `CombatFeedbackController` connects combat / skill events to `CameraEffects` |
| VFX one-shots                 | **Implemented.** `VFXManager` generates pooled hit sparks, block sparks, auras, afterimages, trails, and release blasts |
| Enemy AI                      | **Implemented.** `EnemyAI` FSM handles approach, retreat, hold range, attack, block, dash, Crimson Slash, stun, and death |
| Game over on player/enemy death | **Implemented.** `GameManager` subscribes to both `HealthSystem.OnDeath` events and shows Victory/Defeat |

## Player controls and tuning

### Input map (legacy Input Manager)

| Action | Default binding         |
| ------ | ----------------------- |
| Move   | `A` / `D` or `←` / `→`  |
| Jump   | `Space`                 |
| Dash   | `Left Shift`            |
| Block  | `I` or **Right Mouse**  |
| `Q` `W` `E` | State Skill input chains |
| _(reserved)_ `J` `K` `L` | Basic / heavy / special attacks (future prompt) |

Every binding is a `[SerializeField] KeyCode` (or mouse-button index) on
`PlayerController`, so rebinding is one inspector edit. Input is read with
`GetAxisRaw("Horizontal")` so movement is crisp, not smoothed.

### Recommended default tuning

These values ship in the scene; tweak in the Inspector on **Void Sorcerer →
PlayerController**.

| Group     | Field                 | Default | Notes |
| --------- | --------------------- | ------- | ----- |
| Movement  | `moveSpeed`           | 6       | World units/s |
|           | `acceleration`        | 60      | Units/s² when input is held |
|           | `deceleration`        | 50      | Units/s² when input is released |
|           | `laneZ`               | 0       | Gameplay-plane Z |
| Jump      | `jumpForce`           | 9       | Vertical velocity impulse |
|           | `coyoteTime`          | 0.10    | Late-press grace after walk-off |
|           | `jumpBufferTime`      | 0.12    | Early-press grace before landing |
|           | `gravityMultiplier`   | 1.6     | Extra falling weight |
| Dash      | `dashSpeed`           | 22      | Flat horizontal velocity |
|           | `dashDuration`        | 0.18    | Seconds at dashSpeed |
|           | `dashCooldown`        | 0.55    | After dash ends |
|           | `dashEnergyCost`      | 0       | Emitted in `DashRequested`, picked up by EnergySystem later |
| Block     | `blockMoveMultiplier` | 0.35    | Speed scalar while held |
| Ground    | `groundCheckRadius`   | 0.22    | Sphere at `GroundCheck` |
|           | `groundMask`          | Everything | Tighten when a Ground layer exists |
| Facing    | `facingTurnSpeed`     | 1440    | Degrees/s for the visual flip |
|           | `facingDeadZone`      | 0.05    | Stops flicker right above the enemy |

### State the controller exposes

- `IsGrounded`, `IsAirborne`
- `IsDashing`
- `IsBlocking`
- `IsMovementLocked`
- `FacingDirection` (`+1` or `-1`)
- `CurrentVelocity` (Vector3 from the Rigidbody)
- `MoveInput` (this frame's raw `-1 / 0 / 1`)

Use these directly to drive Animator parameters when animations land —
e.g. `SetFloat("Speed", Mathf.Abs(controller.CurrentVelocity.x))`,
`SetBool("Grounded", controller.IsGrounded)`, `SetBool("Dashing", controller.IsDashing)`.

### Events the controller fires

| Event              | Payload          | Typical subscriber |
| ------------------ | ---------------- | ------------------ |
| `OnJump`           | —                | Animator (`Jump` trigger), VFXManager (dust puff) |
| `OnLand`           | —                | CameraEffects (small shake), Animator (`Land` trigger) |
| `OnDashStart`      | `Vector3` dir    | VFXManager (afterimage trail), CameraEffects (impulse) |
| `OnDashEnd`        | —                | VFXManager (stop trail) |
| `OnBlockStart`/`End` | —              | UIManager (guard icon), Animator (`Blocking` bool) |
| `DashRequested`    | `float` cost     | EnergySystem (`TrySpend` / `Gain` on refund) |

### Public methods for combat / skills to call

- `LockMovement(float duration)` — for attack startup/recovery, hitstun, skill cinematics.
- `SetMovementLocked(bool)` — manual override (cutscene, intro).
- `ForceFaceTarget(Transform)` / `ClearForcedFace()` — for cinematic skill cams.
- `TeleportToLane(Vector3)` — Z is snapped back to `laneZ` automatically.

### Facing convention

`PlayerController` rotates **`visualRoot`** in world space (90° = facing +X,
−90° = facing −X). The physics root is never rotated, so velocity stays
world-space-aligned. `AttackOrigin` and `SkillOrigin` are children of the
root, _not_ `visualRoot` — so combat code must offset by
`FacingDirection` when spawning attacks:

```csharp
Vector3 spawn = controller.AttackOrigin.position
              + Vector3.right * controller.FacingDirection * reach;
```

Or re-parent those anchors under `VisualRoot` if you prefer auto-mirroring
(the foundation deliberately leaves them on the root for clarity).

All managers are reachable through `GameManager.Instance.UI / VFX / Camera`.
Future systems should prefer this over `GameObject.Find`.

## Combat system

### Data flow

```
J pressed → CombatController.RequestAttack
          → OverlapBoxNonAlloc on hittableMask
          → for each unique target with IDamageable:
              ├── Build DamageInfo (damage, knockback, attackType, source)
              ├── If target has IBlockable + IsBlocking + canBeBlocked
              │     → scale damage × blockDamageMultiplier
              │     → scale knockback × blockKnockbackMultiplier
              │     → OnBlocked event
              ├── HealthSystem.TakeDamage(info)
              │     ├── reduces HP, fires OnHealthChanged + OnDamaged
              │     ├── applies knockback via Rigidbody.AddForce (Impulse)
              │     └── fires OnDeath if dead
              └── EnergySystem.NotifyHitLanded / NotifyBlocked
```

### Interfaces

| Interface | Implementer (today) | Purpose |
| --- | --- | --- |
| `IDamageable` | `HealthSystem` | Receive `DamageInfo` |
| `IBlockable` | `PlayerController` | Tell attackers whether block is active |
| `IKnockbackReceiver` | `HealthSystem` | Receive a directional impulse |

The interfaces live in `AnimeFighter.Combat`, so `PlayerController`'s
implementation of `IBlockable` is the one cross-namespace dependency.

### Recommended default combat tuning

| Component | Field | Default | Notes |
| --- | --- | ---: | --- |
| HealthSystem (player) | maxHealth | 100 | |
| HealthSystem (enemy)  | maxHealth | 120 | Slightly tankier sandbag |
| EnergySystem | maxEnergy | 100 | |
|              | regenPerSecond | 4 | Slow trickle |
|              | gainOnHit | 8 | ~12 hits fills the meter |
|              | gainOnBlock | 3 | |
|              | gainOnPerfectDodge | 15 | Future hook |
| CombatController (player) | attackDamage | 8 | Light, before combo scaling |
|                           | attackKnockback | 4 | Impulse force |
|                           | attackRange | 1.0 | Distance from owner centre |
|                           | attackBoxSize | (1.6, 1.4, 0.8) | OverlapBox dimensions |
|                           | attackCooldown | 0.35 | Until next attack possible |
|                           | comboWindow | 0.45 | To chain next hit |
|                           | comboResetTime | 0.9 | Until combo counter clears |
|                           | maxCombo | 3 | |
|                           | comboDamageMultiplier | 1.15 | Per step |
|                           | comboKnockbackMultiplier | 1.25 | Per step |
|                           | blockDamageMultiplier | 0.25 | Damage on a blocked hit |
|                           | blockKnockbackMultiplier | 0.15 | |
|                           | attackMovementLock | 0.18 | Owner stuck during swing |
|                           | canAttackWhileDashing | false | Dash commits |
| CombatController (enemy)  | readKeyboardInput | **false** | AI drives via RequestAttack() |
|                           | attackDamage | 10 | |
|                           | maxCombo | 2 | Simpler chain |
| Rigidbody (enemy)         | m_Drag | 5 | Knockback dissipates naturally |

### Events available for subscribers

| Source | Event | Payload | Typical subscriber |
| --- | --- | --- | --- |
| `HealthSystem`  | `OnHealthChanged` | (current, max) | UI bars |
|                 | `OnDamaged`       | `DamageInfo`   | VFX (hit spark), camera (shake) |
|                 | `OnDeath`         | —              | GameManager → GameOver, AI cleanup |
| `EnergySystem`  | `OnEnergyChanged` | (current, max) | UI bar |
| `CombatController` | `OnAttackStarted` | combo index | Animator trigger |
|                    | `OnAttackHit`     | (DamageInfo, target) | VFX, camera shake, hit-pause, audio |
|                    | `OnAttackWhiffed` | —           | "whoosh" SFX |
|                    | `OnBlocked`       | (DamageInfo, target) | Clang VFX, blue spark |
|                    | `OnComboChanged`  | combo index | HUD combo counter |

### Player taking damage and debug damage

`EnemyAI` can damage the player with melee attacks and Crimson Slash. Extra
test paths:

- Inspector → Void Sorcerer → HealthSystem → ⋮ menu → _Debug/Apply 10 damage_
  (registered via `[ContextMenu]`).
- Or from a temporary script: `gameManager.Player.GetComponent<HealthSystem>().TakeDamage(new DamageInfo(10, ..., ..., 0, null, true, AttackType.Light))`.
- Knockback is applied via `Rigidbody.AddForce`. The player's
  `PlayerController` writes velocity every FixedUpdate, so the player visibly
  does not get pushed back yet — adding hitstun (locking movement for
  `DamageInfo.hitstunDuration`) is the natural follow-up.

### Enemy combat

The enemy GameObject still carries a `CombatController` for symmetry, with
`readKeyboardInput: false` and `playerController: null`. `EnemyAI` owns its
medium-difficulty melee and Crimson Slash hit resolution directly so it can
use enemy-facing, block, dash, windup, and state-reaction logic without
pretending to be a `PlayerController`.

## Crimson Curse AI

`EnemyAI` is a finite state machine:

- `Idle`
- `Approach`
- `Retreat`
- `StrafeOrHoldDistance`
- `Attack`
- `Block`
- `Dash`
- `Skill`
- `Stunned`
- `Dead`

Recommended default tuning:

| Field | Default |
| --- | ---: |
| `reactionTime` | 0.32 |
| `aggression` | 0.58 |
| `preferredRange` | 1.65 |
| `retreatHealthThreshold` | 0.32 |
| `blockChance` | 0.28 |
| `skillUseChance` | 0.34 |
| `attackCooldown` | 1.05 |
| `dashCooldown` | 1.8 |
| `skillCooldown` | 3.2 |
| `skillWindup` | 0.42 |
| `skillDamage` | 18 |

Behavior notes:

- Far away: approaches, with occasional dash-in if aggression wins.
- Close: attacks, blocks pressure, or retreats if too close / low HP.
- Medium range: holds preferred range and sometimes uses Crimson Slash.
- Player `Activation`: more likely to block.
- Player `Charge`: more likely to interrupt with attack, dash-in, or slash.
- Player `ReleaseReady` / `Releasing`: attempts dash-away or panic block.
- Death disables the AI and clears block/action state.

## State Skill system

Void Sorcerer has `InputSequenceDetector` and `StateSkillController` on the
root object. Default chains:

| State transition | Input chain | Window | Energy |
| --- | --- | ---: | ---: |
| Normal -> Activation | `Q -> W -> E` | 1.5s | 10 |
| Activation -> Charge | `W -> E -> Q` | 1.5s | 20 |
| Charge -> Release | `E -> Q -> W` | 1.5s | 35 |

Recommended tuning on `StateSkillController`:

| Field | Default |
| --- | ---: |
| `wrongInputPenaltyEnergy` | 3 |
| `timeoutPenaltyEnergy` | 0 |
| `activationHoldTime` | 0.35 |
| `chargeHoldTime` | 0.50 |
| `releaseWindup` | 0.35 |
| `releaseRecovery` | 0.45 |
| `releaseCooldown` | 3.0 |
| `failDelay` | 0.30 |
| `releaseDamage` | 70 |
| `releaseKnockbackForce` | 16 |
| `releaseRange` | 6.5 |
| `releaseHitboxSize` | `(6.5, 2.25, 1.2)` |

Inspector wiring:

- `sequenceDetector`, `skillOrigin`, player combat components, and enemy target
  auto-resolve from the same GameObject and `GameManager` if left empty.
- Skill logic emits events; `CombatFeedbackController` handles VFX, camera,
  and screen overlay responses.
- UI hooks include skill state, sequence progress, failure, timeout, cooldown,
  screen darkening, and impact flashes.
- Camera hooks use `CameraEffects`: framing, shake, zoom pulse, punch,
  hit-stop, slow motion, release cinematic, and reset.

## Feedback layer

`Managers` has `CombatFeedbackController`, `CameraEffects`, `VFXManager`, and
`UIManager`. Runtime-generated placeholder effects are used until production
prefabs/materials exist.

Recommended defaults:

| Feedback | Default |
| --- | ---: |
| Basic hit stop | 0.045s |
| Basic hit shake | 0.075 intensity for 0.11s |
| Block hit stop | 0.028s |
| Dash afterimage lifetime | 0.18s |
| Slash trail lifetime | 0.18s |
| Activation zoom pulse | FOV 34 for 0.16s |
| Charge shake | 0.05 intensity for 0.18s |
| Release slow motion | 0.35 timeScale during windup |
| Release impact shake | 0.30 intensity for 0.28s |
| Release screen darken | 0.48 alpha |

Runtime effects:

- Hit spark, block spark, enemy red/black hit reaction, and energy gain use a
  small pooled Particle System setup.
- Activation and Charge use persistent blue/purple aura particles and rings.
- Dash uses short-lived transparent mesh ghosts from the player's `VisualRoot`.
- Basic attacks use a generated `TrailRenderer` slash arc from `AttackOrigin`.
- Release uses screen darkening, slow motion, zoom, camera punch, hit flash,
  a purple burst, and a beam line from `SkillOrigin`.

## HUD and readability UI

`UIManager` creates `CombatHUDCanvas` and `ScreenFeedbackCanvas` at runtime when
Inspector references are empty. It subscribes through `GameManager` to:

- `HealthSystem.OnHealthChanged` for player and enemy HP.
- `EnergySystem.OnEnergyChanged` for Cursed Energy.
- `CombatController.OnAttackHit` / `OnComboChanged` for the combo counter.
- `StateSkillController.OnStateChanged`, cooldown, failure, and timeout events.
- `InputSequenceDetector` start/progress/fail/timeout/completed events.

Runtime HUD layout:

| Element | Position / behavior |
| --- | --- |
| Player HP | Top-left, blue delayed damage bar |
| Cursed Energy | Under player HP, purple fill with ready pulse |
| Enemy HP | Top-right, red delayed damage bar |
| Timer placeholder | Top-center |
| State Skill | Bottom-center state panel |
| Input sequence | Bottom-center Q/W/E boxes with progress highlight |
| Cooldown | Bottom-center overlay fill + seconds text |
| Combo | Player side, pops on multi-hit |
| Overlays | Release darken, impact flash, wrong-input flash/shake |

## Final integration pass

`GameManager` is the high-level integration point for the vertical slice:

- Resolves manager components on the `Managers` object if references are empty.
- Snaps actors to prototype spawn positions on Play: player `(-3, 1, 0)`, enemy `(3, 1, 0)`.
- Assigns `PlayerController.EnemyTarget` and `EnemyAI.Target`.
- Subscribes to player and enemy `HealthSystem.OnDeath`.
- Calls `UIManager.ShowGameOver(true/false)` and enters `GameOver`.
- Calls `CameraEffects.ResetEffects()` on boot and match end so time scale and camera state recover safely.

Prototype debug hotkeys are enabled through `GameManager.enableDebugHotkeys`.

| Key | Debug action |
| --- | --- |
| `F1` | Add 35 Cursed Energy |
| `F2` | Damage player for 25 |
| `F3` | Damage enemy for 25 |
| `F4` | Reset State Skill to Normal |
| `F5` | Force enemy death |
| `F6` | Reset camera effects and time scale |

Use these only for local playtesting. Disable `enableDebugHotkeys` before a
non-debug build.

## Complete playtest route

1. Open `Assets/_Project/Scenes/PrototypeArena.unity`.
2. Press Play and confirm the HUD appears.
3. Move with `A/D`, jump with `Space`, dash with `Left Shift`, block with `I` or right mouse.
4. Hit with `J` until the enemy HP bar and combo counter respond.
5. Use `F1` if needed to fill Cursed Energy.
6. Enter `Q -> W -> E`, then `W -> E -> Q`, then `E -> Q -> W`.
7. Confirm Release shows slow motion, darken, camera punch, blast VFX, enemy damage, cooldown, then Normal.
8. Enter a wrong Q/W/E input and confirm only the current sequence resets.
9. Let a started sequence time out and confirm the major state is preserved.
10. Let the enemy hit/block/dash/skill, then kill either character and confirm Victory/Defeat.

## Known limitations

- Runtime VFX and HUD are generated placeholders, not authored prefabs.
- The AI is intentionally medium difficulty and probabilistic; it is not tuned for competitive play.
- Player hitstun is minimal. Knockback is visible, but movement recovery still favors responsiveness.
- No authored animation controller, audio mix, Cinemachine, or post-processing stack is included yet.
- The project uses the legacy Input Manager for speed of iteration.

## Suggested next phase

- Replace placeholder capsules/VFX with authored character meshes, animations, slash prefabs, and audio.
- Move tuning into ScriptableObjects once multiple characters or skills exist.
- Add player hitstun/guard-break rules and clearer enemy telegraph animation.
- Add a restart flow and round timer after the core vertical slice is approved.

## Not yet implemented

- Player hitstun — see _Player taking damage_ above.
- URP / HDRP setup, toon shader, Cinemachine, post-processing.
- Prefabs in `Prefabs/`. Promote scene objects once they stabilise.

## Acceptance checklist (cumulative)

- [x] Folder structure under `Assets/_Project/` matches spec.
- [x] `PrototypeArena.unity` opens.
- [x] Player and enemy capsules are visible with their colour identity.
- [x] Camera frames a side-view.
- [x] `GameManager` is wired to player, enemy, UIManager, VFXManager, CameraEffects.
- [x] All listed scripts exist and compile.
- [x] No `GameObject.Find` in core logic.
- [x] Player walks, jumps, dashes, blocks; auto-faces the enemy.
- [x] Player can damage enemy with `J`; enemy HP decreases; enemy is knocked back.
- [x] Combo advances with timed presses, resets after the window expires.
- [x] Cursed Energy regenerates passively and gains on hit / block.
- [x] Combat is event-driven — UI / VFX / Camera plug in without touching core.
- [x] Q/W/E State Skill chains advance Activation, Charge, and Release.
- [x] Wrong inputs and timeouts reset only the active sequence attempt.
- [x] Release applies Ultimate damage, knockback, recovery, and cooldown.
- [x] Basic hits trigger hit-stop, camera shake, overlay flash, and particles.
- [x] Dash, Activation, Charge, wrong input, and Release have placeholder VFX.
- [x] Camera and time-scale effects reset through `CameraEffects.ResetEffects()`.
- [x] Crimson Curse approaches, retreats, holds range, attacks, blocks, dashes, and uses Crimson Slash.
- [x] Enemy decisions react to player Activation, Charge, ReleaseReady, and Releasing states.
- [x] Enemy death disables AI behavior.
- [x] Runtime HUD shows player HP, enemy HP, Cursed Energy, State Skill, input sequence, combo, cooldown, and overlays.
- [x] GameManager handles player/enemy death, Victory/Defeat UI, spawn placement, actor targeting, and debug hotkeys.

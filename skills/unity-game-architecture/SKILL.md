---
name: unity-game-architecture
description: Guide AI through Unity script architecture, component design patterns, prefab strategy, and ScriptableObject usage. Use when creating or reviewing C# scripts and project structure.
---

# Game Architecture — Script & Component Design Skill

This skill guides you through writing well-structured Unity C# code and organizing project architecture.

## Core Principle: Composition Over Inheritance

Unity's component system is built for composition. Prefer small, focused components over deep inheritance hierarchies.

```
BAD:  Entity → Character → Player → PlayerWithInventory
GOOD: Player GameObject with: Movement, Health, Inventory, PlayerInput components
```

## Script Architecture Patterns

### 1. Single Responsibility Components

Each MonoBehaviour should do one thing well:

```csharp
// GOOD — separated concerns
public class PlayerMovement : MonoBehaviour { }  // handles movement only
public class PlayerHealth : MonoBehaviour { }     // handles health/damage only
public class PlayerInventory : MonoBehaviour { }  // handles items only

// BAD — god class
public class Player : MonoBehaviour { /* movement + health + inventory + input + UI + audio */ }
```

### 2. Manager / Service Pattern

Use singletons sparingly. Prefer explicit references or ScriptableObject events.

```csharp
// Acceptable: Simple manager with lazy init
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}

// Better: ScriptableObject-based shared state (no singleton needed)
[CreateAssetMenu]
public class GameState : ScriptableObject
{
    public int score;
    public int lives;
    public event Action OnStateChanged;
}
```

### 3. ScriptableObject Usage

Use ScriptableObjects for:

| Use Case | Example |
|----------|---------|
| Configuration data | Enemy stats, weapon parameters, level config |
| Shared runtime state | Game state, player progress |
| Event channels | Decoupled communication between systems |
| Inventory/item definitions | Item database, loot tables |

```csharp
// Data container
[CreateAssetMenu(fileName = "EnemyConfig", menuName = "Game/Enemy Config")]
public class EnemyConfig : ScriptableObject
{
    public string enemyName;
    public float health = 100f;
    public float speed = 3f;
    public float damage = 10f;
}

// Event channel
[CreateAssetMenu(fileName = "GameEvent", menuName = "Game/Event")]
public class GameEvent : ScriptableObject
{
    private readonly List<Action> _listeners = new();
    public void Raise() => _listeners.ForEach(l => l?.Invoke());
    public void Register(Action listener) => _listeners.Add(listener);
    public void Unregister(Action listener) => _listeners.Remove(listener);
}
```

### 4. Interface-Based Design

Use interfaces for swappable behaviors:

```csharp
public interface IDamageable
{
    void TakeDamage(float amount);
    float CurrentHealth { get; }
}

public interface IInteractable
{
    string InteractionPrompt { get; }
    void Interact(GameObject interactor);
}
```

## Project Folder Structure

```
Assets/
├── Scripts/
│   ├── Core/           # Managers, singletons, game loop
│   ├── Player/         # Player-specific components
│   ├── Enemies/        # Enemy AI and behavior
│   ├── UI/             # UI controllers and views
│   ├── Systems/        # Reusable systems (inventory, save, audio)
│   ├── Data/           # ScriptableObject definitions
│   └── Utils/          # Extensions, helpers
├── Prefabs/
│   ├── Characters/
│   ├── Environment/
│   ├── UI/
│   └── VFX/
├── ScriptableObjects/  # SO instances (config, events)
├── Scenes/
├── Materials/
├── Textures/
├── Audio/
│   ├── Music/
│   └── SFX/
└── Animations/
```

## Prefab Strategy

### When to Make a Prefab

- Any object that appears more than once
- Any object that should be spawnable at runtime
- Any reusable UI element
- Complex object hierarchies you want to version-control

### Prefab Variants

Use variants for objects that share a base but differ slightly:

```
EnemyBase (Prefab)
├── EnemyMelee (Variant) — overrides speed, adds MeleeAttack
├── EnemyRanged (Variant) — overrides speed, adds RangedAttack
└── EnemyBoss (Variant) — overrides health, scale, adds BossAI
```

### Nested Prefabs

Use nested prefabs for modular construction:

```
Vehicle (Prefab)
├── Body (Prefab)       ← reusable across vehicles
├── Wheel_FL (Prefab)   ← standardized wheel
├── Wheel_FR (Prefab)
└── Engine (Prefab)     ← swappable engine types
```

## Code Review Checklist

When reviewing or writing scripts, verify:

- [ ] No `Update()` with empty body or minimal work (use events/coroutines instead)
- [ ] No `Find()` or `FindObjectOfType()` in Update (cache in Awake/Start)
- [ ] No string-based method calls (`SendMessage`, `Invoke("MethodName")`) — use direct references or events
- [ ] `[SerializeField]` for inspector-exposed private fields instead of public
- [ ] Null checks on serialized references (use `inspect_gameobject` to verify)
- [ ] No magic numbers — use `const`, `[SerializeField]`, or ScriptableObject config
- [ ] Proper namespace usage matching folder structure
- [ ] Uses `UnityEngine.InputSystem` (Keyboard.current, Mouse.current) — **never** legacy `UnityEngine.Input`
- [ ] UI uses UI Toolkit (UIDocument + UXML + USS) — not UGUI for new screen-space UI

## Script Verification Workflow

After creating or editing scripts:

```
1. create_script / script_apply_edits
2. refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)
3. read_console(types=["error"], count=20, include_stacktrace=true)
4. If attaching to object:
   manage_gameobject(action="modify", target="...", components_to_add=["ComponentName"])
5. inspect_gameobject(target="...") → verify no missing references in issues
```

## Common Anti-Patterns

| Anti-Pattern | Problem | Solution |
|-------------|---------|----------|
| God class | One script does everything | Split into focused components |
| Find in Update | Performance killer | Cache references in Awake |
| Public everything | No encapsulation | Use [SerializeField] private |
| String coupling | Fragile, no compile-time checks | Use direct references, enums, SO events |
| Deep inheritance | Rigid, hard to modify | Use composition with interfaces |
| Static everything | Hard to test, tight coupling | Use dependency injection or SO |
| Legacy `Input.GetAxis()` | Compile error with new InputSystem | Use `Keyboard.current`, `Mouse.current`, or Input Actions |
| UGUI for new screen-space UI | Heavier, less flexible than UI Toolkit | Use UIDocument + UXML + USS for new UI |

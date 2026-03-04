---
name: unity-physics-gameplay
description: Guide AI through Unity physics setup, rigidbody configuration, collision detection, InputSystem-based movement patterns, and common gameplay mechanics. Use when implementing physics-based movement, collision systems, or gameplay interactions.
---

# Physics & Gameplay Skill

Guide for setting up physics, collisions, movement, and common gameplay mechanics in Unity.

## Rigidbody Configuration

### Body Types

| Type | Use Case | Moves Via | Collides With |
|------|----------|-----------|---------------|
| Dynamic (default) | Player, enemies, projectiles, physics objects | Forces, velocity | Everything |
| Kinematic (`isKinematic=true`) | Moving platforms, doors, animated objects | Transform/script | Dynamic only (via OnTriggerEnter) |
| Static (no Rigidbody) | Walls, floors, terrain, environment | Doesn't move | Dynamic only |

### Setup Rules

- **Every moving collider needs a Rigidbody** — moving a collider without one forces physics recalculation every frame
- **Use `Interpolate`** on player/camera-followed objects to prevent visual jitter
- **Freeze unused axes** via Constraints — freeze Y rotation on a ground vehicle, freeze Z position on a 2D game
- **Set Collision Detection to Continuous** for fast-moving objects (projectiles) to prevent tunneling
- **Adjust mass** relative to other objects — a 1kg ball shouldn't push a 1000kg car

## Colliders

### Collider Selection

| Collider | Performance | Use For |
|----------|-------------|---------|
| Box | Fastest | Crates, walls, doors, buildings, UI elements |
| Sphere | Fast | Balls, pickups, trigger zones, proximity detection |
| Capsule | Fast | Characters, NPCs, humanoid entities |
| Mesh | Slowest | Complex static environment only (never on moving objects) |
| Composite (multiple primitives) | Good | Complex shapes on moving objects — use child objects with simple colliders |

### Trigger vs Collision

| Feature | Collision | Trigger |
|---------|-----------|---------|
| Physics response | Yes (bounce, push) | No |
| Callback | `OnCollisionEnter/Stay/Exit` | `OnTriggerEnter/Stay/Exit` |
| Inspector setting | `Is Trigger` unchecked | `Is Trigger` checked |
| Use for | Physical interaction | Detection zones, pickups, area effects |
| Requirement | At least one Rigidbody | At least one Rigidbody |

### Collision Matrix

Use layer-based collision filtering via `get_project_settings(categories=["physics"])`:

```
Common layer setup:
- Default (0): Environment/static
- Player (8): Player character
- Enemy (9): Enemy characters
- Projectile (10): Bullets, arrows
- Pickup (11): Collectibles
- Trigger (12): Detection zones

Disable unnecessary collisions:
- Projectile vs Projectile (player bullets don't hit each other)
- Pickup vs Enemy (enemies don't collect pickups)
- Trigger vs Trigger (zones don't interact)
```

Set layers via: `manage_gameobject(action="modify", target="...", layer="Player")`

## Input System (New)

**Always use the new Input System** (`com.unity.inputsystem`) unless the project explicitly uses the legacy Input Manager. Check Project Settings > Player > Active Input Handling.

### Reading Input

```csharp
using UnityEngine.InputSystem;

// Polling (in Update/FixedUpdate)
var keyboard = Keyboard.current;
var mouse = Mouse.current;
var gamepad = Gamepad.current;

// Keyboard
bool jump = keyboard?.spaceKey.wasPressedThisFrame ?? false;
float horizontal = keyboard != null ? (keyboard.dKey.isPressed ? 1 : keyboard.aKey.isPressed ? -1 : 0) : 0;

// Mouse
Vector2 mousePos = mouse?.position.ReadValue() ?? Vector2.zero;
bool fire = mouse?.leftButton.wasPressedThisFrame ?? false;
float scroll = mouse?.scroll.ReadValue().y ?? 0;

// Gamepad
Vector2 leftStick = gamepad?.leftStick.ReadValue() ?? Vector2.zero;
bool gamepadJump = gamepad?.buttonSouth.wasPressedThisFrame ?? false;
```

### Input Actions (Preferred for Production)

Input Actions decouple logic from specific devices. Create an Input Action Asset or define in code:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInput : MonoBehaviour
{
    private PlayerControls _controls; // Generated from Input Action Asset

    private Vector2 _moveInput;
    private bool _jumpPressed;

    void Awake()
    {
        _controls = new PlayerControls();
        _controls.Gameplay.Move.performed += ctx => _moveInput = ctx.ReadValue<Vector2>();
        _controls.Gameplay.Move.canceled += _ => _moveInput = Vector2.zero;
        _controls.Gameplay.Jump.performed += _ => _jumpPressed = true;
    }

    void OnEnable() => _controls.Enable();
    void OnDisable() => _controls.Disable();

    void Update()
    {
        // Use _moveInput and _jumpPressed
        if (_jumpPressed) { /* jump logic */ _jumpPressed = false; }
    }
}
```

### Quick Inline Input (Prototyping)

For rapid prototypes without an Input Action Asset:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class SimplePlayerController : MonoBehaviour
{
    [SerializeField] private float speed = 5f;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float x = 0, z = 0;
        if (kb.wKey.isPressed) z = 1;
        if (kb.sKey.isPressed) z = -1;
        if (kb.aKey.isPressed) x = -1;
        if (kb.dKey.isPressed) x = 1;

        var move = new Vector3(x, 0, z).normalized * speed * Time.deltaTime;
        transform.Translate(move, Space.World);
    }
}
```

## Movement Patterns

### Rigidbody-Based Movement (Recommended for physics games)

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class RigidbodyMovement : MonoBehaviour
{
    [SerializeField] private float speed = 8f;
    [SerializeField] private float jumpForce = 10f;

    private Rigidbody _rb;

    void Awake() => _rb = GetComponent<Rigidbody>();

    void FixedUpdate()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float x = 0, z = 0;
        if (kb.wKey.isPressed) z = 1;
        if (kb.sKey.isPressed) z = -1;
        if (kb.aKey.isPressed) x = -1;
        if (kb.dKey.isPressed) x = 1;

        // Velocity-based (direct control)
        _rb.velocity = new Vector3(x * speed, _rb.velocity.y, z * speed);

        // OR force-based (more physical feel)
        // _rb.AddForce(new Vector3(x, 0, z) * acceleration, ForceMode.Acceleration);
    }

    void Update()
    {
        if (Keyboard.current?.spaceKey.wasPressedThisFrame == true)
            _rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }
}
```

**When to use**: Platformers, racing, physics puzzles, any game where objects should interact physically.

### CharacterController-Based Movement (Recommended for action games)

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class CharacterControllerMovement : MonoBehaviour
{
    [SerializeField] private float speed = 6f;
    [SerializeField] private float gravity = -20f;
    [SerializeField] private float jumpHeight = 2f;

    private CharacterController _controller;
    private Vector3 _velocity;

    void Awake() => _controller = GetComponent<CharacterController>();

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float x = 0, z = 0;
        if (kb.wKey.isPressed) z = 1;
        if (kb.sKey.isPressed) z = -1;
        if (kb.aKey.isPressed) x = -1;
        if (kb.dKey.isPressed) x = 1;

        Vector3 move = (transform.right * x + transform.forward * z) * speed;

        if (_controller.isGrounded && _velocity.y < 0)
            _velocity.y = -2f;

        if (_controller.isGrounded && kb.spaceKey.wasPressedThisFrame)
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

        _velocity.y += gravity * Time.deltaTime;
        move.y = _velocity.y;

        _controller.Move(move * Time.deltaTime);
    }
}
```

**When to use**: FPS, third-person action, RPGs — when you need precise control without physics interference.

### Transform-Based Movement (Simple/2D)

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleMovement : MonoBehaviour
{
    [SerializeField] private float speed = 5f;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float x = 0;
        if (kb.dKey.isPressed) x = 1;
        if (kb.aKey.isPressed) x = -1;

        transform.Translate(Vector3.right * x * speed * Time.deltaTime);
    }
}
```

**When to use**: Simple 2D games, UI elements, non-physics movement. Add a Rigidbody2D (Kinematic) if collision detection is needed.

### Comparison

| Approach | Physics | Ground Detection | Slopes | Precision |
|----------|---------|-------------------|--------|-----------|
| Rigidbody | Full | Raycast or collision | Automatic | Less precise |
| CharacterController | None | Built-in `isGrounded` | Built-in `slopeLimit` | Precise |
| Transform | None | Manual raycast | Manual | Most precise |

## Raycasting

### Common Patterns

```csharp
using UnityEngine.InputSystem;

// Ground check
bool isGrounded = Physics.Raycast(transform.position, Vector3.down, groundDistance, groundLayer);

// Shooting / line of sight
if (Physics.Raycast(camera.position, camera.forward, out RaycastHit hit, maxRange, targetLayer))
{
    // hit.point, hit.normal, hit.collider.gameObject
}

// Mouse picking (new Input System)
var mouse = Mouse.current;
if (mouse != null)
{
    Ray ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
    if (Physics.Raycast(ray, out RaycastHit hit, 100f, interactableLayer))
    {
        hit.collider.GetComponent<IInteractable>()?.Interact();
    }
}
```

### Raycast Best Practices

- **Always use layer masks** — `Physics.Raycast(origin, dir, dist, layerMask)` instead of hitting everything
- **Use `Physics.RaycastNonAlloc`** in Update to avoid garbage allocation
- **SphereCast** for wider detection (melee attacks, thick line-of-sight)
- **Avoid raycasting every frame** if the result doesn't change — cache and update on intervals

## 2D vs 3D Physics

| Feature | 3D | 2D |
|---------|----|----|
| Components | Rigidbody, BoxCollider, etc. | Rigidbody2D, BoxCollider2D, etc. |
| Callbacks | OnCollisionEnter(Collision) | OnCollisionEnter2D(Collision2D) |
| Raycasting | Physics.Raycast | Physics2D.Raycast |
| Settings | Physics settings | Physics2D settings (separate!) |
| Gravity | Vector3 (default: 0, -9.81, 0) | float (default: -9.81, Y-axis only) |
| Constraints | Freeze X/Y/Z position and rotation | Freeze X/Y position, Z rotation |

**Critical**: 3D and 2D physics are completely separate systems. 3D colliders do not interact with 2D colliders.

### 2D-Specific Tips

- Use `Rigidbody2D.bodyType` instead of `isKinematic` (Dynamic, Kinematic, Static)
- Use `CompositeCollider2D` with `TilemapCollider2D` for efficient tilemap collision
- Set `Rigidbody2D.gravityScale = 0` for top-down games
- Use `Rigidbody2D.simulated = false` to disable physics without removing the component

## Common Gameplay Mechanics

### Jump

```csharp
using UnityEngine.InputSystem;

// Rigidbody jump
bool jumpInput = Keyboard.current?.spaceKey.wasPressedThisFrame ?? false;
if (isGrounded && jumpInput)
    rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);

// CharacterController jump
if (isGrounded && jumpInput)
    velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
```

### Knockback

```csharp
Vector3 knockDir = (target.position - source.position).normalized;
targetRb.AddForce(knockDir * knockForce, ForceMode.Impulse);
```

### Projectile

```csharp
// On spawn
rb.velocity = transform.forward * speed;
// OR
rb.AddForce(transform.forward * speed, ForceMode.VelocityChange);
// Set Collision Detection to Continuous Dynamic for fast projectiles
```

## Physics Settings Checklist

Review via `get_project_settings(categories=["physics", "physics2d"])`:

| Setting | Recommendation |
|---------|---------------|
| Fixed Timestep | 0.02 (50Hz) default, 0.01 for precision games |
| Gravity | -9.81 for realistic, -20 to -30 for snappy platformers |
| Default Solver Iterations | 6 default, increase for stacking/joints |
| Layer Collision Matrix | Disable all unnecessary layer pairs |
| Auto Sync Transforms | Disable for performance (manual sync when needed) |
| Reuse Collision Callbacks | Enable to reduce GC |

## Common Issues

| Problem | Cause | Fix |
|---------|-------|-----|
| Objects pass through each other | Too fast, discrete collision detection | Use Continuous collision detection, increase Fixed Timestep |
| Jittery movement | Transform and physics fighting, no interpolation | Move in FixedUpdate, enable Rigidbody Interpolate |
| Object won't stop sliding | No friction, no drag | Add PhysicMaterial with friction, increase Rigidbody drag |
| Collisions not detected | Missing Rigidbody, wrong layers, 2D/3D mismatch | Ensure at least one Rigidbody, check collision matrix, don't mix 2D/3D |
| OnTriggerEnter not firing | No Rigidbody on either object | Add Rigidbody (can be Kinematic) to at least one object |
| Character slides down slopes | CharacterController `slopeLimit` too high | Reduce slopeLimit, implement slope friction |
| Physics feels floaty | Gravity too weak | Increase gravity magnitude (-20 to -40 for responsive feel) |
| Raycast misses objects | Wrong layer mask, collider disabled, query triggers | Check layer mask, enable collider, set `Physics.queriesHitTriggers` |

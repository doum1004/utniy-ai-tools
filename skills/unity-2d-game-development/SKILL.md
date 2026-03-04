---
name: unity-2d-game-development
description: Guide for building 2D games in Unity — sprites, tilemaps, movement, sorting, camera, lighting, and animation. Use when working on 2D Unity projects.
---

# 2D Game Development Skill

Guide for building 2D games in Unity — sprites, tilemaps, movement, sorting, camera, lighting, and animation.

## Project Setup for 2D

Always verify the project is configured for 2D before building:

```
1. get_project_settings(categories=["rendering", "physics2d"])
   → confirm orthographic camera, 2D physics gravity
2. analyze_scene()
   → check "is_2d" flag
3. manage_scene(action="get_active")
   → verify Main Camera is orthographic
```

### 2D vs 3D Key Differences

| Setting | 3D | 2D |
|---------|----|----|
| Camera | Perspective | Orthographic |
| Physics | Rigidbody + Collider | Rigidbody2D + Collider2D |
| Rendering order | Z position + render queue | Sorting Layer + Order in Layer |
| Gravity | Vector3 (Y-axis) | float (Y-axis only, `gravityScale` per body) |
| Raycasting | `Physics.Raycast` | `Physics2D.Raycast` |
| Movement callbacks | `OnCollisionEnter` | `OnCollisionEnter2D` |

---

## Sprites

### SpriteRenderer Setup

```
manage_components(action="add", target="Player", component_type="SpriteRenderer")
manage_components(action="set_property", target="Player",
    component_type="SpriteRenderer",
    property_name="sortingLayerName", property_value="Characters")
manage_components(action="set_property", target="Player",
    component_type="SpriteRenderer",
    property_name="sortingOrder", property_value=0)
```

### Sprite Import Settings (guide user)

| Setting | Recommended Value | Why |
|---------|-------------------|-----|
| Texture Type | Sprite (2D and UI) | Enables sprite slicing and packing |
| Pixels Per Unit | 16, 32, 64, or 100 | Must match across all assets — pick one and stick to it |
| Filter Mode | Point (no filter) | Pixel art — avoids blurring. Use Bilinear for smooth art |
| Compression | None or low | Sprites are small — quality matters more than size |
| Max Size | Match sprite sheet size | Avoid unnecessary downscaling |

### Sprite Atlas

For performance, pack sprites into atlases to reduce draw calls:

```
Guide user to:
Window > 2D > Sprite Atlas > Create
Add sprites/folders to the atlas
Enable "Include in Build"
```

Key rule: **one atlas per gameplay context** (e.g. `characters.spriteatlas`, `ui.spriteatlas`, `environment.spriteatlas`). Never mix atlas groups.

---

## Tilemap

### Tilemap Layer Structure

```
Grid (GameObject with Grid component)
├── Ground (Tilemap + TilemapRenderer + TilemapCollider2D + CompositeCollider2D)
├── Platforms (Tilemap + TilemapRenderer + TilemapCollider2D + CompositeCollider2D)
├── Decoration (Tilemap + TilemapRenderer — no collider, visual only)
└── Foreground (Tilemap + TilemapRenderer — rendered above player)
```

### Setting Up a Tilemap Layer

```
1. Create Grid:
   manage_gameobject(action="create", name="Grid")
   manage_components(action="add", target="Grid", component_type="Grid")

2. Create Tilemap child:
   manage_gameobject(action="create", name="Ground", parent="Grid")
   manage_components(action="add", target="Ground", component_type="Tilemap")
   manage_components(action="add", target="Ground", component_type="TilemapRenderer")

3. Add physics:
   manage_components(action="add", target="Ground", component_type="TilemapCollider2D")
   manage_components(action="add", target="Ground", component_type="CompositeCollider2D")
   manage_components(action="set_property", target="Ground",
       component_type="TilemapCollider2D", property_name="usedByComposite", property_value=true)
   manage_components(action="set_property", target="Ground",
       component_type="Rigidbody2D", property_name="bodyType", property_value=2)
   # bodyType 2 = Static — required for CompositeCollider2D

4. Configure sorting:
   manage_components(action="set_property", target="Ground",
       component_type="TilemapRenderer", property_name="sortingLayerName", property_value="Ground")
```

### Tilemap Sorting Layer Convention

| Layer | Order | Contents |
|-------|-------|----------|
| Background | -10 | Sky, parallax backgrounds |
| Ground | 0 | Main floor/platform tiles |
| Characters | 10 | Player, enemies, NPCs |
| Effects | 20 | Particles, projectiles |
| Foreground | 30 | Trees, pillars that overlap characters |
| UI | 100 | HUD elements (use Canvas instead) |

### Tile Painting (guide user — visual task)

```
1. Open Tile Palette: Window > 2D > Tile Palette
2. Create palette: Create New Palette → name it (e.g. "Ground Tiles")
3. Drag sprite sheet into palette window to auto-generate tiles
4. Select tile, select tilemap layer in Hierarchy, paint in Scene view
```

AI cannot paint tiles programmatically — guide the user through the Tile Palette workflow.

---

## Sorting System

Sorting determines render order. Incorrect sorting causes characters to appear behind walls, effects to render under the ground, etc.

### Two-Level System

```
Sorting Layer (broad category) → Order in Layer (fine-tuning within layer)
```

### Setting Sorting Layers

```
get_project_settings(categories=["rendering"])  → check existing layers

Add layers via: execute_menu_item(menu_path="Edit/Project Settings...")
→ guide user to Tags and Layers > Sorting Layers
```

Or set on a SpriteRenderer directly:
```
manage_components(action="set_property", target="Enemy",
    component_type="SpriteRenderer",
    property_name="sortingLayerName", property_value="Characters")
manage_components(action="set_property", target="Enemy",
    component_type="SpriteRenderer",
    property_name="sortingOrder", property_value=5)
```

### Transparency Sort Mode

For **top-down** games, set the camera's transparency sort to sort by Y position (objects higher on screen appear behind objects lower on screen):

```
get_project_settings(categories=["rendering"])
→ guide user: Edit > Project Settings > Graphics > Transparency Sort Mode = Custom Axis
→ Transparency Sort Axis = (0, 1, 0)
```

---

## 2D Camera

### Orthographic Camera Setup

```
manage_components(action="set_property", target="Main Camera",
    component_type="Camera", property_name="orthographic", property_value=true)
manage_components(action="set_property", target="Main Camera",
    component_type="Camera", property_name="orthographicSize", property_value=5)
# orthographicSize = half the vertical height in world units
# At PPU=16, size=5 shows 160 pixels vertically (10 world units)
```

### Orthographic Size Formula

```
orthographicSize = (screenHeight / 2) / pixelsPerUnit

Examples at 16 PPU:
  720p  → 720 / 2 / 16 = 22.5  (too large for pixel art — scale up sprites)
  360px → 360 / 2 / 16 = 11.25
  180px → 180 / 2 / 16 = 5.625 (common for pixel art games)
```

### Camera Follow (script)

```csharp
// Simple smooth follow — attach to Main Camera
public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float smoothSpeed = 5f;
    [SerializeField] private Vector3 offset = new Vector3(0, 0, -10f);

    private void LateUpdate()
    {
        if (target == null) return;
        var desired = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desired, smoothSpeed * Time.deltaTime);
    }
}
```

### Pixel Perfect Camera (pixel art games)

```
manage_components(action="add", target="Main Camera",
    component_type="UnityEngine.U2D.PixelPerfectCamera")
manage_components(action="set_property", target="Main Camera",
    component_type="PixelPerfectCamera",
    property_name="assetsPPU", property_value=16)
manage_components(action="set_property", target="Main Camera",
    component_type="PixelPerfectCamera",
    property_name="refResolutionX", property_value=320)
manage_components(action="set_property", target="Main Camera",
    component_type="PixelPerfectCamera",
    property_name="refResolutionY", property_value=180)
```

---

## 2D Movement Patterns

### Side-Scroller (Platformer)

```csharp
[RequireComponent(typeof(Rigidbody2D))]
public class PlatformerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpForce = 10f;
    [SerializeField] private LayerMask groundLayer;

    private Rigidbody2D _rb;
    private bool _isGrounded;

    private void Awake() => _rb = GetComponent<Rigidbody2D>();

    private void Update()
    {
        float input = Input.GetAxisRaw("Horizontal");
        _rb.velocity = new Vector2(input * moveSpeed, _rb.velocity.y);

        _isGrounded = Physics2D.CircleCast(transform.position, 0.2f, Vector2.down, 0.1f, groundLayer);

        if (Input.GetButtonDown("Jump") && _isGrounded)
            _rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

        // Flip sprite based on direction
        if (input != 0)
            transform.localScale = new Vector3(Mathf.Sign(input), 1, 1);
    }
}
```

### Top-Down (RPG / Twin-Stick)

```csharp
[RequireComponent(typeof(Rigidbody2D))]
public class TopDownController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 4f;
    private Rigidbody2D _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.gravityScale = 0;  // No gravity in top-down
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    private void FixedUpdate()
    {
        var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        _rb.velocity = input.normalized * moveSpeed;
    }
}
```

### Isometric (Top-Down with depth sorting)

Same as top-down movement, but with Y-axis transparency sorting enabled (see Sorting System section) and sprite pivot set to bottom-center.

---

## 2D Animation

### Animator Setup for Sprite Sheet

```
Guide user:
1. Select sprite sheet in Project → Sprite Editor → Slice by grid (match tile size)
2. Select sliced sprites → drag to Scene → Unity auto-creates Animator + AnimationClip
3. In Animator window: create parameters (Speed: Float, IsGrounded: Bool, IsAttacking: Trigger)
4. Create transitions with conditions
```

### Blend Tree for Directional Movement (top-down)

```
In Animator:
1. Right-click → Create State → From New Blend Tree
2. Blend Type: 2D Simple Directional
3. Parameters: MoveX (Float), MoveY (Float)
4. Add motions: Walk_Up, Walk_Down, Walk_Left, Walk_Right
   with positions: (0,1), (0,-1), (-1,0), (1,0)
```

### Driving Animation from Script

```csharp
_animator.SetFloat("Speed", _rb.velocity.magnitude);
_animator.SetBool("IsGrounded", _isGrounded);
_animator.SetFloat("MoveX", input.x);
_animator.SetFloat("MoveY", input.y);
```

---

## 2D Lighting (URP)

Requires Universal Render Pipeline with 2D renderer.

### Light2D Types

| Type | Use Case |
|------|----------|
| Global Light 2D | Ambient — base brightness for the whole scene |
| Point Light 2D | Torches, lamps, explosions |
| Spot Light 2D | Flashlights, directional cones |
| Freeform Light 2D | Custom shape light areas |

### Basic Setup

```
1. Global ambient light:
   manage_gameobject(action="create", name="Global Light")
   manage_components(action="add", target="Global Light",
       component_type="UnityEngine.Rendering.Universal.Light2D")
   manage_components(action="set_property", target="Global Light",
       component_type="Light2D", property_name="lightType", property_value=3)
   # lightType 3 = Global
   manage_components(action="set_property", target="Global Light",
       component_type="Light2D", property_name="intensity", property_value=0.3)

2. Point light (torch):
   manage_gameobject(action="create", name="Torch Light", parent="Torch")
   manage_components(action="add", target="Torch Light",
       component_type="UnityEngine.Rendering.Universal.Light2D")
   manage_components(action="set_property", target="Torch Light",
       component_type="Light2D", property_name="pointLightOuterRadius", property_value=3)
```

### Shadow Casters

```
manage_components(action="add", target="Wall",
    component_type="UnityEngine.Rendering.Universal.ShadowCaster2D")
```

---

## Common 2D Issues

| Problem | Cause | Fix |
|---------|-------|-----|
| Sprite bleeding / pixel gaps between tiles | Sprite atlas padding or pivot issues | Enable `Padding` in Sprite Atlas; set filter to Point; ensure PPU is consistent |
| Character renders behind tilemap | Sorting layer or order wrong | Set character SpriteRenderer sorting layer above tilemap layer |
| Pixel art looks blurry | Filter mode is Bilinear/Trilinear | Set all pixel art sprites to Filter Mode: Point (no filter) |
| Physics not working | Using 3D components (Rigidbody not Rigidbody2D) | Replace with 2D equivalents — 3D and 2D physics don't interact |
| Jittery camera on pixel art | Camera not snapping to pixel grid | Add Pixel Perfect Camera component; set `cropFrameX/Y` to fit |
| Object not affected by 2D Light | No Normal Map or wrong material | Sprite material must use a Lit sprite shader (URP/2D Renderer/Lit) |
| Tilemap collider has gaps | Individual tile colliders not merged | Add CompositeCollider2D + enable `usedByComposite` on TilemapCollider2D |
| Top-down sorting wrong | No Y-axis transparency sort | Set Graphics > Transparency Sort Mode to Custom Axis (0, 1, 0) |
| Sprites Z-fighting | Same Z position and sorting values | Separate sorting layers or use small Z offsets (0.001) |
| Animation stutters | Frame rate doesn't match sample rate | Set animation clip Sample Rate to match your target FPS (12 for retro, 24 for smooth) |

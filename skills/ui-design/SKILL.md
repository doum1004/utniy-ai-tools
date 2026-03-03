---
name: ui-design
description: Guide AI through Unity UI design — UGUI canvas setup, anchoring, layout groups, responsive design, UI Toolkit basics, and common UI patterns. Use when building HUD, menus, or interactive UI elements.
---

# UI/UX Design Skill

Guide for building Unity UI — canvas setup, layout, responsive design, and interaction patterns.

## UGUI vs UI Toolkit

| Factor | UGUI (Canvas) | UI Toolkit (UIDocument) |
|--------|---------------|------------------------|
| Best for | In-game HUD, world-space UI, quick prototypes | Complex editor tools, menu-heavy UIs, web-like layouts |
| Layout model | RectTransform + anchors | Flexbox (USS stylesheets) |
| Styling | Per-component properties | USS (like CSS), shared stylesheets |
| Runtime support | Full (all Unity versions) | Full from Unity 2023.1+, partial in 2021/2022 |
| Data binding | Manual or third-party | Built-in binding system |
| Recommendation | Default choice for most game UI | Prefer for complex, data-driven UI in 2023+ |

## Canvas Setup

### Render Modes

| Mode | Use Case | Notes |
|------|----------|-------|
| Screen Space - Overlay | HUD, menus, always-on-top UI | Simplest, renders last, no camera needed |
| Screen Space - Camera | UI that needs post-processing or depth interaction | Assign a dedicated UI camera |
| World Space | In-game panels, VR/AR interfaces, billboards | Needs explicit sizing, attach to GameObjects |

### Canvas Best Practices

1. **One canvas per update frequency** — separate always-visible HUD from rarely-changed menus to reduce rebuild cost
2. **Canvas Scaler** — use `Scale With Screen Size` with reference resolution (1920x1080 for landscape, 1080x1920 for portrait), match width or height based on game orientation
3. **Graphic Raycaster** — disable `Raycast Target` on non-interactive elements (Text, decorative Images) to reduce input processing cost
4. **Sorting** — use `Sort Order` on Canvas, not sibling order, for reliable layering across canvases

## Anchoring and Responsive Layout

### Anchor Presets

| Element | Anchor | Why |
|---------|--------|-----|
| Health bar (top-left) | Top-left | Stays in corner on any resolution |
| Minimap (bottom-right) | Bottom-right | Corner-pinned |
| Title text (top-center) | Top-center stretch horizontal | Scales width with screen |
| Full-screen panel | Stretch-stretch | Fills entire canvas |
| Center popup | Middle-center | Stays centered |

### Key Rules

- **Always set anchors before positioning** — position values are relative to anchor
- **Use stretch anchors for containers**, fixed anchors for icons/buttons
- **Pivot affects rotation and scaling origin** — set pivot to (0.5, 0.5) for center-based scaling
- **Test at multiple resolutions**: 1280x720, 1920x1080, 2560x1440, and mobile aspect ratios (9:16, 9:19.5)

## Layout Groups

| Component | Use Case |
|-----------|----------|
| Horizontal Layout Group | Button rows, icon bars, tabs |
| Vertical Layout Group | Lists, menus, settings panels |
| Grid Layout Group | Inventory grids, card layouts, icon grids |
| Layout Element | Override min/preferred/flexible size on children |
| Content Size Fitter | Auto-size to content (text boxes, scroll content) |

### Layout Tips

- Set `Child Force Expand` to false unless you want elements to fill all available space
- Use `Spacing` for consistent gaps instead of padding on individual children
- Combine `Content Size Fitter` + `Vertical Layout Group` on scroll view content for dynamic lists
- Nest layout groups: Vertical for rows, Horizontal inside each row for columns

## Safe Area Handling

For mobile devices with notches and rounded corners:

```
Workflow:
1. Create a "SafeArea" panel as child of Canvas (stretch-stretch anchors)
2. Attach a SafeArea script that adjusts RectTransform to Screen.safeArea
3. Place all interactive UI inside this panel
4. Background/decorative elements can remain outside
```

## Event System

- **Every scene with UI needs an EventSystem** — Unity creates one with the first Canvas, but verify it exists
- **One EventSystem per scene** — multiple causes input conflicts
- **Input Module**: use `Input System UI Input Module` for new Input System, `Standalone Input Module` for legacy
- For **world-space UI interaction**: add `Physics Raycaster` to the camera, ensure UI elements have colliders or use `Graphic Raycaster` with a UI camera

## Common UI Patterns

### Modal Popup
```
Canvas (Overlay, Sort Order: 10)
├── Dimmer (stretch, Image with alpha 0.5 black, Raycast Target: true to block input)
└── Panel (center anchor)
    ├── Title Text
    ├── Body Text
    └── Button Row (Horizontal Layout Group)
        ├── Cancel Button
        └── Confirm Button
```

### Scroll List
```
ScrollView (stretch anchors)
├── Viewport (with Mask)
│   └── Content (Vertical Layout Group + Content Size Fitter, vertical fit: Preferred)
│       ├── Item Prefab
│       ├── Item Prefab
│       └── ...
└── Scrollbar
```

### Health Bar
```
HealthBar (top-left anchor)
├── Background (Image, dark color)
├── Fill (Image, type: Filled, fill method: Horizontal)
└── Label (Text, "100/100")
```

## Workflow

```
1. Determine render mode based on use case
2. Create Canvas with Canvas Scaler (Scale With Screen Size)
3. Set up anchor structure — containers stretch, elements pin
4. Add layout groups for repetitive elements
5. Wire interactions (Button.onClick, Toggle.onValueChanged)
6. Test at target resolutions via Game view aspect ratio dropdown
7. Run analyze_scene to check for issues
8. Screenshot to verify visual layout
```

## Common Issues

| Problem | Cause | Fix |
|---------|-------|-----|
| UI not receiving clicks | Missing EventSystem or Raycast Target disabled | Add EventSystem, enable Raycast Target on interactive elements |
| UI renders behind 3D objects | Canvas render mode or sort order | Use Overlay or increase Sort Order |
| Layout jumping on content change | Missing Content Size Fitter | Add Content Size Fitter to dynamic containers |
| Blurry text | Canvas Scaler reference resolution mismatch | Match reference resolution to target, use TextMeshPro |
| Too many draw calls from UI | Each unique material/texture = 1 draw call | Use Sprite Atlas, minimize unique materials, disable Raycast Target on decorative elements |
| UI scales wrong on different devices | Anchors set incorrectly | Use stretch anchors for containers, test multiple resolutions |
| Scroll view not scrolling | Content has no Content Size Fitter or Layout Group | Add Vertical Layout Group + Content Size Fitter to content |
| World Space UI not clickable | Missing Physics Raycaster on camera | Add Physics Raycaster to camera that sees the UI |

## UI Toolkit Quick Reference

For projects using UI Toolkit (UIDocument):

| Concept | UGUI Equivalent | UI Toolkit |
|---------|-----------------|------------|
| Canvas | Canvas component | UIDocument component |
| Layout | RectTransform + Layout Groups | UXML + USS flexbox |
| Styling | Per-component inspector | USS stylesheets (`.uss`) |
| Events | Button.onClick | RegisterCallback<ClickEvent> |
| Templating | Prefabs | UXML templates |
| Data binding | Manual | SerializedObject binding |

### USS Layout Basics
- Default flow is **column (vertical)** — set `flex-direction: row` for horizontal
- Use `flex-grow: 1` to fill available space
- Use `justify-content` and `align-items` for alignment (same as CSS flexbox)
- Use `padding`, `margin`, `border-width` for spacing

---
name: level-design
description: Guide AI through scene composition, spatial layout, lighting, camera setup, and iterative level design using analysis feedback loops. Use when building or reviewing Unity scenes.
---

# Level Design — Scene Composition Skill

This skill guides you through building, reviewing, and iterating on Unity scenes with quality feedback.

## Core Principle: Build, Analyze, Iterate

Never build a scene and walk away. Always follow the feedback loop:

```
1. Build/modify the scene
2. analyze_scene(include_details=true) → review stats
3. manage_scene(action="screenshot") → visual check
4. Identify issues → fix them
5. Repeat until quality bar is met
```

## Scene Composition Checklist

Before considering a scene complete, verify:

- [ ] Clear visual hierarchy (important objects are prominent)
- [ ] Proper lighting (no pure black areas, no blown-out highlights)
- [ ] Camera positioned to frame the scene well
- [ ] No missing component references (check analyze_scene → missing_component_count)
- [ ] Hierarchy is organized (use empty GameObjects as folders, max depth < 6)
- [ ] Objects are named descriptively (no "Cube (4)", "GameObject (12)")

## Spatial Layout Guidelines

### 3D Scenes

- **Ground plane**: Always establish a ground/floor first. Use a scaled Plane or terrain.
- **Scale reference**: Place a 1x1x1 cube early as a human-scale reference (roughly 1 meter).
- **Grouping**: Parent related objects under named empties: "Environment", "Characters", "UI", "Lighting".
- **Spacing**: Objects shouldn't overlap unintentionally. Use `inspect_gameobject` to check positions.
- **Y-axis**: Ensure objects sit on the ground (y=0 for floor-level objects), not floating or clipping.

### 2D Scenes

- **Sorting layers**: Set up distinct sorting layers for Background, Midground, Characters, Foreground, UI.
- **Z-depth**: Even in 2D, use z-position or sorting order to control render order.
- **Camera**: Use orthographic projection. Set size based on your target resolution.
- **Pixel perfect**: Consider pixel-per-unit consistency across sprites.

## Lighting Workflow

### 3D Lighting Setup

```
1. Start with a single Directional Light (sun)
   - Rotation: (50, -30, 0) for classic 3/4 lighting
   - Shadows: Soft Shadows enabled
   - Intensity: 1.0

2. Add ambient/fill
   - Render Settings → Environment Lighting
   - Or add a low-intensity secondary directional light

3. Add accent lights for points of interest
   - Point Lights for localized glow
   - Spot Lights for dramatic focus

4. Review with analyze_scene → light_breakdown
   - Aim for < 4 realtime lights per area for performance
```

### 2D Lighting

- Use URP 2D Lights if available (Global Light 2D for ambient, Point Light 2D for local).
- Without URP: rely on sprite colors and post-processing for mood.

## Camera Setup

### 3D Camera Checklist

| Setting | Recommended |
|---------|-------------|
| Clear Flags | Skybox or Solid Color |
| FOV | 60 for gameplay, 40-50 for cinematic |
| Near Clip | 0.1 (not 0.01 — causes z-fighting) |
| Far Clip | 1000 max unless needed |

### 2D Camera Checklist

| Setting | Recommended |
|---------|-------------|
| Projection | Orthographic |
| Size | Match to target resolution / PPU |
| Clear Flags | Solid Color |

## Post-Build Review Workflow

After constructing a scene, run this sequence:

```
1. analyze_scene(include_details=true)
   → Check: missing_component_count == 0
   → Check: max_hierarchy_depth < 8
   → Check: inactive_gameobjects (any forgotten disabled objects?)

2. manage_scene(action="screenshot")
   → Visually verify composition, lighting, object placement

3. For each suspicious object from analyze_scene:
   inspect_gameobject(target="ObjectName")
   → Check issues array for problems

4. get_project_settings(categories=["rendering", "quality"])
   → Verify settings match scene needs (shadows, anti-aliasing)
```

## Common Issues and Fixes

| Issue | Detection | Fix |
|-------|-----------|-----|
| Objects floating | Screenshot review | Set y-position to ground level |
| Missing shadows | analyze_scene → light details | Enable shadows on directional light |
| Pink materials | Screenshot shows magenta | Shader mismatch — check render pipeline |
| Z-fighting | Flickering surfaces in screenshot | Increase near clip plane or separate surfaces |
| Cluttered hierarchy | max_hierarchy_depth > 8 | Reorganize with parent groups |
| Unnamed objects | Root object names check | Rename with meaningful names |

## Scene Organization Template

```
Scene Root
├── --- Environment ---
│   ├── Ground
│   ├── Walls
│   └── Props
├── --- Characters ---
│   ├── Player
│   └── NPCs
├── --- Lighting ---
│   ├── Sun (Directional)
│   └── AccentLights
├── --- Cameras ---
│   └── Main Camera
├── --- UI ---
│   └── Canvas
└── --- Systems ---
    └── GameManager
```

Use `---` prefix naming convention for organizational empty GameObjects to visually separate groups in the hierarchy.

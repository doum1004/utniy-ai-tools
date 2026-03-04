---
name: unity-ui-design
description: Guide AI through Unity UI design with a UI Toolkit-first policy for HUD/menu/screen-space UI. Use UGUI Canvas only for world-space UI or explicit legacy maintenance.
---

# UI/UX Design Skill

Guide for building Unity UI with a strict default: use UI Toolkit for all new screen-space UI. Use UGUI only for world-space UI or explicit legacy constraints.

## Default Policy (Required)

- For new HUD, menus, overlays, settings, and screen-space UI: **always use UI Toolkit** (`UIDocument` + `UXML` + `USS`).
- Do **not** create a `Canvas` unless the user explicitly asks for world-space UI, VR/AR interaction, or legacy UGUI maintenance.
- If requirements are ambiguous, choose UI Toolkit and state that default briefly.
- If a Canvas already exists in a legacy project, prefer extending it only when migration risk is high or the user requests compatibility.

## UI Toolkit vs UGUI

| Factor | UI Toolkit (UIDocument) | UGUI (Canvas) |
|--------|------------------------|---------------|
| **Recommendation** | **Preferred for all new UI** | Legacy projects or world-space UI |
| Layout model | Flexbox (USS stylesheets) | RectTransform + anchors |
| Styling | USS (CSS-like), shared stylesheets | Per-component properties |
| Runtime support | Full from Unity 2021.2+ | All Unity versions |
| Data binding | Built-in binding system (2023.2+) | Manual or third-party |
| Templating | UXML templates (reusable, composable) | Prefabs |
| Input System | Works with both old and new Input System | Requires InputSystemUIInputModule for new |
| Performance | Retained-mode, minimal rebuilds | Canvas rebuild on any change in group |
| Best for | All game UI, menus, HUD, editor tools | World-space panels, VR/AR UI, legacy |

## UI Toolkit — Full Workflow

### Architecture

```
UIDocument (component on GameObject)
├── PanelSettings (ScriptableObject asset — screen-space settings)
├── Source Asset (UXML — layout/structure)
└── USS Stylesheets (styling, referenced from UXML)
```

### 1. Create UXML Layout

```xml
<?xml version="1.0" encoding="utf-8"?>
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:uie="UnityEditor.UIElements">
    <Style src="GameHUD.uss" />
    <ui:VisualElement name="hud-root" class="hud-root">
        <ui:VisualElement name="top-bar" class="top-bar">
            <ui:Label name="score-label" text="Score: 0" class="hud-text" />
            <ui:Label name="wave-label" text="Wave 1" class="hud-text" />
        </ui:VisualElement>
        <ui:VisualElement name="health-bar" class="health-bar">
            <ui:VisualElement name="health-fill" class="health-fill" />
        </ui:VisualElement>
        <ui:VisualElement name="center-message" class="center-message hidden">
            <ui:Label name="message-text" text="" class="message-text" />
        </ui:VisualElement>
        <ui:Button name="pause-btn" text="Pause" class="btn" />
    </ui:VisualElement>
</ui:UXML>
```

### 2. Create USS Stylesheet

```css
.hud-root {
    flex-grow: 1;
    padding: 16px;
}

.top-bar {
    flex-direction: row;
    justify-content: space-between;
    margin-bottom: 8px;
}

.hud-text {
    font-size: 24px;
    color: white;
    -unity-font-style: bold;
    text-shadow: 1px 1px 2px rgba(0, 0, 0, 0.8);
}

.health-bar {
    width: 200px;
    height: 20px;
    background-color: rgba(60, 60, 60, 0.8);
    border-radius: 4px;
    overflow: hidden;
}

.health-fill {
    width: 100%;
    height: 100%;
    background-color: rgb(46, 204, 64);
    transition: width 0.3s ease;
}

.center-message {
    position: absolute;
    left: 0; right: 0; top: 0; bottom: 0;
    align-items: center;
    justify-content: center;
}

.message-text {
    font-size: 48px;
    color: white;
    -unity-font-style: bold;
}

.hidden {
    display: none;
}

.btn {
    padding: 8px 16px;
    font-size: 16px;
    background-color: rgba(0, 0, 0, 0.6);
    color: white;
    border-width: 1px;
    border-color: white;
    border-radius: 4px;
    align-self: flex-end;
}

.btn:hover {
    background-color: rgba(255, 255, 255, 0.2);
}
```

### 3. Create PanelSettings Asset

PanelSettings **must** be created as an asset (not via `ScriptableObject.CreateInstance` at runtime — that breaks shader references).

```csharp
// Editor script to create PanelSettings
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class CreatePanelSettings
{
    [MenuItem("Tools/Create Panel Settings")]
    public static void Create()
    {
        var ps = ScriptableObject.CreateInstance<PanelSettings>();
        ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        ps.referenceResolution = new Vector2Int(1920, 1080);
        ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        ps.match = 0.5f;
        AssetDatabase.CreateAsset(ps, "Assets/UI/DefaultPanelSettings.asset");
        AssetDatabase.SaveAssets();
        Debug.Log("PanelSettings created at Assets/UI/DefaultPanelSettings.asset");
    }
}
```

### 4. UI Controller Script (Runtime Binding)

```csharp
using UnityEngine;
using UnityEngine.UIElements;

public class GameHUDController : MonoBehaviour
{
    private UIDocument _doc;
    private Label _scoreLabel;
    private Label _waveLabel;
    private VisualElement _healthFill;
    private VisualElement _centerMessage;
    private Label _messageText;

    void OnEnable()
    {
        _doc = GetComponent<UIDocument>();
        var root = _doc.rootVisualElement;

        _scoreLabel = root.Q<Label>("score-label");
        _waveLabel = root.Q<Label>("wave-label");
        _healthFill = root.Q("health-fill");
        _centerMessage = root.Q("center-message");
        _messageText = root.Q<Label>("message-text");

        root.Q<Button>("pause-btn").RegisterCallback<ClickEvent>(OnPauseClicked);
    }

    public void UpdateScore(int score) =>
        _scoreLabel.text = $"Score: {score}";

    public void UpdateWave(int wave) =>
        _waveLabel.text = $"Wave {wave}";

    public void UpdateHealth(float normalized)
    {
        _healthFill.style.width = Length.Percent(normalized * 100f);
        var color = Color.Lerp(Color.red, new Color(0.18f, 0.8f, 0.25f), normalized);
        _healthFill.style.backgroundColor = color;
    }

    public void ShowMessage(string text)
    {
        _messageText.text = text;
        _centerMessage.RemoveFromClassList("hidden");
    }

    public void HideMessage() =>
        _centerMessage.AddToClassList("hidden");

    private void OnPauseClicked(ClickEvent evt) =>
        Time.timeScale = Time.timeScale > 0 ? 0 : 1;
}
```

### 5. Wire UIDocument in Scene

Use an Editor script to wire UIDocument, PanelSettings, and UXML together:

```csharp
[MenuItem("Tools/Setup UI Document")]
public static void SetupUIDocument()
{
    var uiGO = new GameObject("UIDocument");
    var doc = uiGO.AddComponent<UIDocument>();

    doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/DefaultPanelSettings.asset");
    doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/GameHUD.uxml");

    uiGO.AddComponent<GameHUDController>();
    EditorUtility.SetDirty(uiGO);
}
```

### 6. UI Toolkit Setup via MCP

```
1. create_script(path="Assets/UI/GameHUD.uxml", contents="<UXML content>")
2. create_script(path="Assets/UI/GameHUD.uss", contents="<USS content>")
3. create_script(path="Assets/Editor/SetupUI.cs", contents="<Editor script>")
4. create_script(path="Assets/Scripts/UI/GameHUDController.cs", contents="<controller>")
5. refresh_unity(mode="force", compile="request", wait_for_ready=true)
6. read_console(types=["error"])
7. execute_menu_item(menu_path="Tools/Create Panel Settings")
8. execute_menu_item(menu_path="Tools/Setup UI Document")
9. manage_scene(action="save")
```

### USS Layout Reference

| Property | Values | Notes |
|----------|--------|-------|
| `flex-direction` | `column` (default), `row` | Main axis direction |
| `flex-grow` | `0` (default), `1`, etc. | Fill available space |
| `flex-shrink` | `1` (default), `0` | Prevent shrinking |
| `flex-wrap` | `nowrap` (default), `wrap` | Allow wrapping |
| `justify-content` | `flex-start`, `center`, `flex-end`, `space-between`, `space-around` | Main axis alignment |
| `align-items` | `stretch` (default), `flex-start`, `center`, `flex-end` | Cross axis alignment |
| `align-self` | `auto`, `flex-start`, `center`, `flex-end`, `stretch` | Override parent alignment |
| `position` | `relative` (default), `absolute` | Positioning mode |
| `display` | `flex` (default), `none` | Visibility toggle |
| `overflow` | `visible` (default), `hidden` | Clip children |
| `transition` | `property duration easing` | Animate changes |

### Common UI Toolkit Patterns

#### Modal Popup

```xml
<ui:VisualElement name="modal-overlay" class="modal-overlay">
    <ui:VisualElement name="modal-panel" class="modal-panel">
        <ui:Label name="modal-title" text="Paused" class="modal-title" />
        <ui:Button name="resume-btn" text="Resume" class="btn" />
        <ui:Button name="quit-btn" text="Quit" class="btn btn-danger" />
    </ui:VisualElement>
</ui:VisualElement>
```

```css
.modal-overlay {
    position: absolute;
    left: 0; right: 0; top: 0; bottom: 0;
    background-color: rgba(0, 0, 0, 0.6);
    align-items: center;
    justify-content: center;
}

.modal-panel {
    background-color: rgb(40, 40, 40);
    padding: 32px;
    border-radius: 8px;
    min-width: 300px;
    align-items: center;
}
```

#### Scroll View

```xml
<ui:ScrollView name="item-list" class="scroll-view">
    <!-- Items added dynamically via C# -->
</ui:ScrollView>
```

```csharp
var scrollView = root.Q<ScrollView>("item-list");
foreach (var item in items)
{
    var row = new VisualElement();
    row.AddToClassList("list-row");
    row.Add(new Label(item.Name));
    row.Add(new Label(item.Value.ToString()));
    scrollView.Add(row);
}
```

#### Health Bar (USS-driven)

```csharp
public void SetHealth(float normalized)
{
    var fill = root.Q("health-fill");
    fill.style.width = Length.Percent(normalized * 100f);
}
```

### UI Toolkit + InputSystem Integration

UI Toolkit works with both the legacy and new Input System. For new InputSystem:

- **No extra configuration needed** for UI Toolkit in screen space — it automatically receives pointer/touch events
- Use `RegisterCallback<PointerDownEvent>`, `RegisterCallback<ClickEvent>`, etc.
- For keyboard navigation, add `Focusable = true` to elements and use `RegisterCallback<NavigationSubmitEvent>`
- For gamepad: UI Toolkit supports `NavigationMoveEvent`, `NavigationSubmitEvent`, `NavigationCancelEvent`

```csharp
// Gamepad/keyboard navigation example
var button = root.Q<Button>("start-btn");
button.focusable = true;
button.RegisterCallback<NavigationSubmitEvent>(evt => StartGame());
```

## UGUI (Explicit Exception Only)

Use UGUI only when you need world-space UI (VR/AR, in-game panels) or are maintaining an explicitly legacy Canvas-based project.

### Canvas Setup

#### Render Modes

| Mode | Use Case | Notes |
|------|----------|-------|
| Screen Space - Overlay | HUD, menus, always-on-top UI | Simplest, renders last, no camera needed |
| Screen Space - Camera | UI that needs post-processing or depth interaction | Assign a dedicated UI camera |
| World Space | In-game panels, VR/AR interfaces, billboards | Needs explicit sizing, attach to GameObjects |

#### Canvas Best Practices

1. **One canvas per update frequency** — separate always-visible HUD from rarely-changed menus to reduce rebuild cost
2. **Canvas Scaler** — use `Scale With Screen Size` with reference resolution (1920x1080 for landscape, 1080x1920 for portrait), match width or height based on game orientation
3. **Graphic Raycaster** — disable `Raycast Target` on non-interactive elements (Text, decorative Images) to reduce input processing cost
4. **Sorting** — use `Sort Order` on Canvas, not sibling order, for reliable layering across canvases

### Anchoring and Responsive Layout

#### Anchor Presets

| Element | Anchor | Why |
|---------|--------|-----|
| Health bar (top-left) | Top-left | Stays in corner on any resolution |
| Minimap (bottom-right) | Bottom-right | Corner-pinned |
| Title text (top-center) | Top-center stretch horizontal | Scales width with screen |
| Full-screen panel | Stretch-stretch | Fills entire canvas |
| Center popup | Middle-center | Stays centered |

#### Key Rules

- **Always set anchors before positioning** — position values are relative to anchor
- **Use stretch anchors for containers**, fixed anchors for icons/buttons
- **Pivot affects rotation and scaling origin** — set pivot to (0.5, 0.5) for center-based scaling
- **Test at multiple resolutions**: 1280x720, 1920x1080, 2560x1440, and mobile aspect ratios (9:16, 9:19.5)

### Layout Groups

| Component | Use Case |
|-----------|----------|
| Horizontal Layout Group | Button rows, icon bars, tabs |
| Vertical Layout Group | Lists, menus, settings panels |
| Grid Layout Group | Inventory grids, card layouts, icon grids |
| Layout Element | Override min/preferred/flexible size on children |
| Content Size Fitter | Auto-size to content (text boxes, scroll content) |

### UGUI + InputSystem

For UGUI with the new Input System:

1. **Replace** `Standalone Input Module` with `Input System UI Input Module` on the EventSystem
2. Set the `Actions Asset` to your project's Input Action Asset (or use the default UI actions)
3. `Button.onClick`, `Toggle.onValueChanged`, etc. work the same regardless of Input System

```csharp
using UnityEngine.InputSystem.UI;

// Ensure EventSystem uses the new Input Module
var eventSystem = FindObjectOfType<EventSystem>();
if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
{
    Destroy(eventSystem.GetComponent<StandaloneInputModule>());
    eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
}
```

## Event System

- **Every scene with UGUI needs an EventSystem** — Unity creates one with the first Canvas, but verify it exists
- **One EventSystem per scene** — multiple causes input conflicts
- **Input Module**: use `Input System UI Input Module` for new Input System, `Standalone Input Module` for legacy
- UI Toolkit does **not** use EventSystem — it has its own event dispatching
- For **world-space UI interaction**: add `Physics Raycaster` to the camera, ensure UI elements have colliders or use `Graphic Raycaster` with a UI camera

## Safe Area Handling

For mobile devices with notches and rounded corners:

**UGUI:**
```
1. Create a "SafeArea" panel as child of Canvas (stretch-stretch anchors)
2. Attach a SafeArea script that adjusts RectTransform to Screen.safeArea
3. Place all interactive UI inside this panel
```

**UI Toolkit:**
```csharp
// In OnEnable or after panel is attached
var safeArea = Screen.safeArea;
var root = _doc.rootVisualElement;
root.style.paddingLeft = safeArea.x;
root.style.paddingRight = Screen.width - safeArea.xMax;
root.style.paddingTop = Screen.height - safeArea.yMax;
root.style.paddingBottom = safeArea.y;
```

## Common Issues

| Problem | Cause | Fix |
|---------|-------|-----|
| UI Toolkit renders nothing | PanelSettings created at runtime via `CreateInstance` | Create as asset via `AssetDatabase.CreateAsset()` in Editor script |
| UI not receiving clicks (UGUI) | Missing EventSystem or Raycast Target disabled | Add EventSystem, enable Raycast Target on interactive elements |
| UI not receiving clicks (UI Toolkit) | Element has `picking-mode: ignore` or is behind another element | Set `picking-mode: position`, check z-order |
| UI renders behind 3D objects | Canvas render mode or sort order | Use Overlay or increase Sort Order |
| Blurry text | Canvas Scaler reference resolution mismatch | Match reference resolution, use TextMeshPro (UGUI) or font-size in USS |
| USS styles not applying | Missing `<Style src="..."/>` in UXML or wrong path | Add `<Style>` tag, verify path is relative to UXML location |
| UI Toolkit transitions not working | Property not animatable or `transition` not set | Only animatable properties work; set `transition` in USS |
| Too many draw calls from UGUI | Each unique material/texture = 1 draw call | Use Sprite Atlas, minimize unique materials |
| MCP screenshot doesn't capture UI Toolkit | Known limitation — overlay not captured | Play test manually, or capture Game view via Screen.CaptureScreenshot |

## Workflow Summary

```
1. Default to UI Toolkit for all new screen-space UI
2. For UI Toolkit: Create UXML + USS → PanelSettings asset → UIDocument → Controller script
3. Use UGUI only as an explicit exception: Canvas (render mode) → Canvas Scaler → Layout → interactions
4. Test at target resolutions via Game view aspect ratio dropdown
5. Integrate with InputSystem (UI Toolkit: automatic, UGUI: InputSystemUIInputModule)
6. Run analyze_scene to check for issues
7. Screenshot / play test to verify
```

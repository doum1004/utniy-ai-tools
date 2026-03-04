---
name: testing-debugging
description: Guide AI through Unity test writing (EditMode/PlayMode), error analysis, debugging workflows, and console log interpretation using MCP tools. Use when writing tests, investigating bugs, or analyzing runtime errors.
---

# Testing & Debugging Skill

Guide for writing Unity tests, analyzing errors, and debugging workflows using MCP tools.

## Test Types

| Type | Runs In | Use For | Speed |
|------|---------|---------|-------|
| EditMode | Editor (no Play) | Pure logic, ScriptableObjects, serialization, editor tools, asset validation | Fast |
| PlayMode | Play mode runtime | MonoBehaviour lifecycle, physics, coroutines, scene integration | Slower |

**Default to EditMode tests** — they are faster, more reliable, and don't require scene setup.

## Running Tests via MCP

```
1. run_tests(mode="EditMode")              → returns job_id
2. get_test_job(job_id="...", wait_timeout=60) → returns results when done
```

For targeted runs:
```
run_tests(mode="EditMode", test_names=["MyTests.CalculateDamage_WithArmor_ReducesDamage"])
run_tests(mode="PlayMode", category="Integration")
```

## Writing EditMode Tests

### Test Structure (Arrange-Act-Assert)

```csharp
using NUnit.Framework;

[TestFixture]
public class DamageCalculatorTests
{
    [Test]
    public void CalculateDamage_WithArmor_ReducesDamage()
    {
        // Arrange
        var calculator = new DamageCalculator();
        float rawDamage = 100f;
        float armor = 25f;

        // Act
        float result = calculator.Calculate(rawDamage, armor);

        // Assert
        Assert.AreEqual(75f, result, 0.01f);
    }
}
```

### What to Test in EditMode

| Category | Examples |
|----------|----------|
| Pure logic | Damage formulas, stat calculations, inventory math |
| Data validation | ScriptableObject field ranges, config parsing |
| Serialization | JSON/binary save-load roundtrips |
| String processing | Localization keys, path formatting |
| Collections | Custom data structures, sorting, filtering |
| State machines | State transitions, guard conditions |

### Test Assembly Setup

```
Tests/
  Editor/
    MyProject.Tests.asmdef    (references main assembly, Editor-only, UNITY_INCLUDE_TESTS)
    CalculatorTests.cs
    InventoryTests.cs
  Runtime/
    MyProject.RuntimeTests.asmdef  (PlayMode tests)
    PlayerControllerTests.cs
```

Assembly definition must have:
- `overrideReferences: true`
- `precompiledReferences: ["nunit.framework.dll"]`
- `defineConstraints: ["UNITY_INCLUDE_TESTS"]`
- Reference to the assembly being tested

## Writing PlayMode Tests

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class PlayerMovementTests
{
    [UnityTest]
    public IEnumerator Player_MovesForward_WhenInputApplied()
    {
        // Arrange
        var player = new GameObject("Player");
        var rb = player.AddComponent<Rigidbody>();
        var mover = player.AddComponent<PlayerMover>();
        var startPos = player.transform.position;

        // Act
        mover.Move(Vector3.forward);
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        // Assert
        Assert.Greater(player.transform.position.z, startPos.z);

        // Cleanup
        Object.Destroy(player);
    }
}
```

### PlayMode Test Patterns

- Use `[UnityTest]` with `IEnumerator` return type for tests that need frames to pass
- Use `[UnitySetUp]` and `[UnityTearDown]` for async setup/teardown
- Always clean up created GameObjects in teardown or end of test
- Use `yield return null` to wait one frame, `yield return new WaitForFixedUpdate()` for physics

## Play-Testing via MCP (LLM as QA Tester)

Three tools let AI agents interact with and observe the game at runtime:

### simulate_input — Interact with the game

```
simulate_input(action="key_press", key="W", duration=2.0)   → hold W for 2 seconds
simulate_input(action="key_press", key="Space")              → tap space
simulate_input(action="mouse_click", position=[400, 300])    → click at screen coords
simulate_input(action="mouse_drag", from=[100, 100], to=[500, 300])
```

Key names: `W`, `A`, `S`, `D`, `Space`, `Return`, `Escape`, `LeftShift`, `F1`-`F15`, `UpArrow`, etc.
Requires Play mode — call `manage_editor(action="play")` first.

### read_runtime_state — Observe the game

```
read_runtime_state()                                          → FPS, time, object counts
read_runtime_state(target="Player")                           → position, components, physics
read_runtime_state(target="Player", fields=["Rigidbody.velocity", "health"])  → specific fields
```

Returns live FPS, frame count, active scene. When targeting a GameObject: position, rotation, scale, component list. Rigidbody velocity/angular velocity and Animator state are auto-included.

### capture_gameplay — Watch the game over time

```
capture_gameplay(duration=5, interval=1.0)     → 5 screenshots, 1 per second
capture_gameplay(duration=10, interval=2.0, max_resolution=320)  → smaller images, longer capture
```

Returns a sequence of screenshots with timestamps, FPS, and any console errors at each frame.

### QA Test Loop

```
1. manage_editor(action="play")
2. simulate_input(action="key_press", key="W", duration=3.0)   → walk forward
3. capture_gameplay(duration=3, interval=1.0)                   → observe movement
4. read_runtime_state(target="Player")                          → check position changed
5. read_console(types=["error"])                                → check for errors
6. manage_editor(action="stop")
→ "Player moved from (0,0,0) to (0,0,8.5). No errors. Movement looks correct."
```

## Debugging Workflow

### Standard Debug Loop

```
1. read_console(types=["error","warning"], count=20)    → identify errors
2. inspect_gameobject(target="ProblemObject")            → check component state
3. analyze_scene()                                       → find missing refs, anomalies
4. Fix the issue (edit script, modify component, etc.)
5. refresh_unity()                                       → recompile
6. read_console(types=["error"], count=5)                → verify fix
```

### Console Log Analysis

When reading console output, prioritize:

| Priority | Type | Action |
|----------|------|--------|
| 1 | **Compilation errors** | Fix immediately — nothing works until these are resolved |
| 2 | **Runtime exceptions** (NullReferenceException, MissingComponentException) | Inspect the object, check component references |
| 3 | **Warnings** (deprecated API, performance) | Address after errors are clear |
| 4 | **Log messages** | Informational, use for tracing logic flow |

### Common Error Patterns

| Error | Likely Cause | Investigation |
|-------|-------------|---------------|
| `NullReferenceException` | Unassigned field, destroyed object, wrong path in Find | `inspect_gameobject` to check serialized fields; verify object exists with `find_gameobjects` |
| `MissingComponentException` | Accessing removed/never-added component | `manage_components(action="get_all")` on the target |
| `Can't add component because class doesn't exist` | Script compilation error, namespace mismatch | `read_console` for compile errors; check script class name matches filename |
| `The object you want to instantiate is null` | Prefab reference lost, wrong asset path | `manage_asset(action="info")` to verify path; check serialized fields |
| `SerializationException` | Missing `[Serializable]`, wrong field types | Read the script, verify serialization attributes |
| `StackOverflowException` | Infinite recursion, property self-reference | Read script for recursive calls |
| `IndexOutOfRangeException` | Array/list access beyond bounds | Check collection sizes in inspector |

### Missing Reference Detection

```
1. analyze_scene()  → check "missing_references" count
2. If > 0, inspect flagged objects:
   inspect_gameobject(target="ObjectWithMissing")
3. Fix: re-assign references, remove broken components, or fix script errors
```

## Test-Driven Workflow

For new features, follow this order:

```
1. Write a failing test that defines the expected behavior
2. create_script to create the implementation
3. refresh_unity() to compile
4. run_tests(mode="EditMode") to verify the test fails correctly
5. Implement the feature
6. refresh_unity() + run_tests to verify it passes
7. read_console to check for warnings
```

## Test Naming Convention

Use the pattern: `MethodName_Condition_ExpectedResult`

```
CalculateDamage_WithZeroArmor_ReturnsFullDamage
CalculateDamage_WithNegativeInput_ClampsToZero
Inventory_AddItem_IncreasesCount
Inventory_RemoveLast_ReturnsEmptyState
PlayerHealth_TakeDamage_BelowZero_Dies
```

## Parameterized Tests

```csharp
[TestCase(100f, 0f, 100f)]
[TestCase(100f, 50f, 50f)]
[TestCase(0f, 50f, 0f)]
[TestCase(100f, 100f, 0f)]
public void CalculateDamage_VariousInputs_ReturnsExpected(float damage, float armor, float expected)
{
    var result = DamageCalculator.Calculate(damage, armor);
    Assert.AreEqual(expected, result, 0.01f);
}
```

## Debugging Tips

- **Isolate the problem**: Use `find_gameobjects` to narrow down which objects are affected
- **Check editor state first**: `unity://editor/state` — if compiling, wait before acting
- **Inspect before modifying**: Always `inspect_gameobject` or `manage_components(action="get_all")` before changing components
- **One fix at a time**: Make one change, refresh, check console, verify — don't batch unrelated fixes
- **Screenshot after visual fixes**: Use `manage_scene(action="screenshot")` to confirm visual changes

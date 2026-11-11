# Authoring VFX Graph from code (internal APIs)

VFX Graph has no public authoring API. The VFX, Shader Graph and URP editor packages declare
`InternalsVisibleTo("Unity.VisualEffectGraph.EditorTests")`, so an editor assembly with **exactly that name**
can use `UnityEditor.VFX` internals. Keep that asmdef editor-only and don't put tests in it.

These APIs can change between VFX Graph versions. Proven on 17.6. If a newer version fails to compile, read the
package source under `Library/PackageCache/com.unity.visualeffectgraph@*/Editor/` — the names below are the search terms.

## Creating a graph

```csharp
VisualEffectAssetEditorUtility.CreateNewAsset(path);           // empty .vfx
var resource = VisualEffectResource.GetResourceAtPath(path);
var graph = resource.GetGraph() ?? resource.CreateGraph();
// ... add contexts/blocks/operators ...
resource.WriteAssetWithSubAssets();
AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
```

Models are ScriptableObjects added as children: `var m = ScriptableObject.CreateInstance<T>(); parent.AddChild(m);`

| Purpose | Type | Notes |
|---|---|---|
| Event | `VFXBasicEvent` | `SetSettingValue("eventName", "OnPlay")` |
| Spawner | `VFXBasicSpawner` + `VFXSpawnerBurst` (slots `Count`, `Delay`) or `VFXSpawnerConstantRate` (slot `Rate`) | `spawner.LinkFrom(evt)` |
| Initialize | `VFXBasicInitialize` | `init.LinkFrom(spawner)`; `init.GetData().SetSettingValue("capacity", (uint)n)`; `init.space = VFXSpace.World` for trails |
| Update | `VFXBasicUpdate` | blocks: `Drag` (slot `dragCoefficient`), `Gravity` |
| Output | `VFXPlanarPrimitiveOutput` | settings `blendMode` (`VFXAbstractRenderedOutput.BlendMode`), `uvMode` (`VFXAbstractParticleOutput.UVMode.Flipbook`), `flipbookBlendFrames`; slots `mainTexture`, `flipBookSize` (`new FlipBook { x, y }`) |
| Set attribute | `SetAttribute` | settings `attribute`, `Source` (`Slot`/`Source`), `Random` (`Off`/`Uniform`/`PerComponent`), `Composition` (`Overwrite`/`Multiply`/`Add`); random slots are `A`/`B` |
| Curve over life | `AttributeFromCurve` | settings `attribute`, `Composition`, `SampleMode = OverLife`; slot named like the attribute |
| Orientation | `Orient` | `mode`: FaceCameraPlane, AlongVelocity, Advanced (`axes = AxesPair.ZY`; slots[0] = Z / normal, slots[1] = Y) |
| Read an attribute | `VFXAttributeParameter` | settings `attribute`, `location` (`Current` / `Source`); link with `slot.Link(param.outputSlots[0])` |

Slots: find by name on `((IVFXSlotContainer)model).inputSlots`; names sometimes have a leading underscore. Set
`slot.value`, using the VFX wrapper types for spaceable values: `(Position)v`, `(DirectionType)v`, `(Vector)v`.
A Local-space position slot (`slot.space = VFXSpace.Local`) in a World-space system spawns at the VisualEffect's
transform — this is how trails follow pooled projectiles.

## Patterns used by the generator

- **Ground quad**: `Orient` Advanced ZY, Z = up, Y = forward (random `angleZ` spins it on the ground).
- **Ground quad along a direction**: same, but link Y to a `VFXAttributeParameter("direction", Current)`; store
  the event's `direction` in Initialize with `SetAttribute direction Source`.
- **Velocity from the event direction**: link `SetAttribute velocity` slot to the direction parameter, then
  multiply by a random speed and add a per-component random spread.
- **Flipbook animation**: `AttributeFromCurve texIndex` Overwrite, linear 0 → frames−0.01 over life.
- **Random frame**: `SetAttribute texIndex` Uniform 0 → frames−0.01 in Initialize.
- **Fixed colour layer**: `SetAttribute color` Slot (instead of Source) before the colour boost multiply.

## Runtime notes

- One long-lived `VisualEffect` per effect type, triggered with `SendEvent("OnPlay", attr)` and a cached
  `VFXEventAttribute` — hundreds of hits per second cost one component per type.
- `VisualEffect.aliveParticleCount` is read back asynchronously; poll it across frames.

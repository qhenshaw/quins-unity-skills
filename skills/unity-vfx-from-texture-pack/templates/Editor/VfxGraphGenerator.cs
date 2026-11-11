// Generates VFX Graph assets (OutDir/VFX_*.vfx) from the textures and flipbooks in
// TexDir (see constants below).
// VFX Graph has no public authoring API. This assembly is named "Unity.VisualEffectGraph.EditorTests"
// because the VFX, Shader Graph and URP editor packages grant that name InternalsVisibleTo access — it is NOT a test assembly.
// Run it from the menu: Tools > VFX > Generate VFX Graphs.
// Re-running overwrites the generated graphs. After generation they are ordinary VFX Graph assets:
// open and tweak them in the VFX Graph editor (and stop re-running this script for that asset).
//
// Burst graphs follow the contract used by EffectsManager.Play():
//   Event "OnPlay" carries position / color / size / direction as spawn attributes.
//   Each effect is several layers (systems) that all start from that one event.
// Trail graphs (VFX_Trail_*) have no event: they spawn continuously and sit on projectile prefabs.
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.VFX;
using UnityEditor.VFX.Block;
using UnityEngine.VFX;

namespace VfxToolkit.Editor
{
    static class VfxGraphGenerator
    {
        // Folder holding the texture pack (edit for your project).
        public const string TexDir = "Assets/VFX/Textures/";
        public const string OutDir = "Assets/VFX/Graphs";

        public enum Frames { None, Animate, Random }
        public enum Facing { Camera, Velocity, Ground, GroundAlongDirection }
        public enum Motion { None, Sphere, Up, Flat, Direction }
        public enum SizeCurve { Constant, Shrink, Grow, Pop }

        /// <summary>One particle system inside an effect graph.</summary>
        public class Layer
        {
            public string texture;              // file in TexDir (null = default round particle)
            public int cols = 1, rows = 1;
            public Frames frames = Frames.None;
            public bool alphaBlend;             // false = additive
            public Facing facing = Facing.Camera;
            public Motion motion = Motion.None;
            public int count = 1;
            public float rate;                  // > 0: continuous spawn (trails), no event
            public float delay;
            public Vector2 lifetime = new Vector2(0.3f, 0.4f);
            public Vector2 speed = Vector2.zero;
            public Vector2 sizeMul = Vector2.one;
            public SizeCurve sizeCurve = SizeCurve.Constant;
            public float growFrom = 0.3f;       // Grow/Pop start size
            public float colorBoost = 1.5f;
            public float drag;
            public bool randomRotation = true;
            public Vector2 scale = Vector2.one; // non-uniform quad scale (x, y)
            public Vector3 offset;              // added to spawn position
            public float spread = 0.25f;        // random added to Direction motion
            public bool fadeIn;                 // alpha 0 → 1 → 0 instead of 1 → 0
            public Vector3? constantDirection;  // trails: fixed (local) direction instead of the event's
            public uint capacity = 512;
            public Color tint = Color.white;    // trails only (bursts take their colour from the event)
            public Color? fixedColor;           // bursts: ignore the event colour (e.g. neutral smoke)
        }

        public class Spec
        {
            public string name;
            public bool worldSpace;             // trails simulate in world space so particles stay behind
            public Layer[] layers;
        }

        // ------------------------------------------------------------------ graph building helpers

        static T Add<T>(VFXModel parent) where T : VFXModel
        {
            var m = ScriptableObject.CreateInstance<T>();
            parent.AddChild(m);
            return m;
        }

        static VFXSlot Slot(VFXModel m, string name)
        {
            var slots = ((IVFXSlotContainer)m).inputSlots;
            var s = slots.FirstOrDefault(x => x.name == name) ?? slots.FirstOrDefault(x => string.Equals(x.name.TrimStart((char)95), name, StringComparison.OrdinalIgnoreCase));
            if (s == null) throw new Exception($"{m.GetType().Name} has no slot '{name}'. Slots: " + string.Join(",", slots.Select(x => x.name)));
            return s;
        }

        static SetAttribute SetAttr(VFXContext ctx, string attr, SetAttribute.ValueSource source = SetAttribute.ValueSource.Slot,
            RandomMode random = RandomMode.Off, AttributeCompositionMode comp = AttributeCompositionMode.Overwrite)
        {
            var b = ScriptableObject.CreateInstance<SetAttribute>();
            b.SetSettingValue("attribute", attr);
            b.SetSettingValue("Source", source);
            b.SetSettingValue("Random", random);
            b.SetSettingValue("Composition", comp);
            ctx.AddChild(b);
            return b;
        }

        static void Curve(VFXContext ctx, string attr, AnimationCurve curve, AttributeCompositionMode comp = AttributeCompositionMode.Multiply)
        {
            var b = ScriptableObject.CreateInstance<AttributeFromCurve>();
            b.SetSettingValue("attribute", attr);
            b.SetSettingValue("Composition", comp);
            b.SetSettingValue("SampleMode", AttributeFromCurve.CurveSampleMode.OverLife);
            ctx.AddChild(b);
            Slot(b, attr).value = curve;
        }

        static VFXAttributeParameter AttributeParam(VFXGraph graph, string attr, VFXAttributeLocation location, Vector2 pos)
        {
            var p = ScriptableObject.CreateInstance<VFXAttributeParameter>();
            p.SetSettingValue("attribute", attr);
            p.SetSettingValue("location", location);
            p.position = pos;
            graph.AddChild(p);
            return p;
        }

        static Texture2D Texture(string file)
        {
            string path = TexDir + file;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new Exception("Missing VFX texture " + path);
            // Effects textures: clamp (flipbooks bleed otherwise), capped at 2048 to keep memory sane.
            bool dirty = importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize > 2048 || !importer.alphaIsTransparency
                         || importer.textureType != TextureImporterType.Default;
            if (dirty)
            {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 2048;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void BuildLayer(VFXGraph graph, VFXBasicEvent evt, Layer l, int index, bool worldSpace)
        {
            float x = index * 420f;
            var spawner = Add<VFXBasicSpawner>(graph);
            spawner.position = new Vector2(x, -150);
            if (l.rate > 0)
            {
                var constant = ScriptableObject.CreateInstance<VFXSpawnerConstantRate>();
                spawner.AddChild(constant);
                Slot(constant, "Rate").value = l.rate;
            }
            else
            {
                var burst = ScriptableObject.CreateInstance<VFXSpawnerBurst>();
                spawner.AddChild(burst);
                Slot(burst, "Count").value = (float)l.count;
                Slot(burst, "Delay").value = l.delay;
                spawner.LinkFrom(evt);
            }

            var init = Add<VFXBasicInitialize>(graph);
            init.position = new Vector2(x, 50);
            init.LinkFrom(spawner);
            init.GetData().SetSettingValue("capacity", l.capacity);
            if (worldSpace) init.space = VFXSpace.World;

            bool trail = l.rate > 0;
            if (trail)
            {
                // Spawn at the VisualEffect's own transform (a Local-space position slot is converted to world).
                var p = SetAttr(init, "position");
                var slot = Slot(p, "position");
                slot.space = VFXSpace.Local;
                slot.value = (Position)Vector3.zero;
                var c = SetAttr(init, "color");
                Slot(c, "color").value = new Vector3(l.tint.r, l.tint.g, l.tint.b) * l.colorBoost;
                var s = SetAttr(init, "size", SetAttribute.ValueSource.Slot, RandomMode.Uniform);
                Slot(s, "A").value = l.sizeMul.x;
                Slot(s, "B").value = l.sizeMul.y;
            }
            else
            {
                SetAttr(init, "position", SetAttribute.ValueSource.Source);
                if (l.fixedColor.HasValue)
                {
                    var fc = SetAttr(init, "color");
                    Slot(fc, "color").value = new Vector3(l.fixedColor.Value.r, l.fixedColor.Value.g, l.fixedColor.Value.b);
                }
                else SetAttr(init, "color", SetAttribute.ValueSource.Source);
                var boost = SetAttr(init, "color", SetAttribute.ValueSource.Slot, RandomMode.Off, AttributeCompositionMode.Multiply);
                Slot(boost, "color").value = Vector3.one * l.colorBoost;
                SetAttr(init, "size", SetAttribute.ValueSource.Source);
                var sizeRand = SetAttr(init, "size", SetAttribute.ValueSource.Slot, RandomMode.Uniform, AttributeCompositionMode.Multiply);
                Slot(sizeRand, "A").value = l.sizeMul.x;
                Slot(sizeRand, "B").value = l.sizeMul.y;
            }
            if (l.offset != Vector3.zero)
            {
                var off = SetAttr(init, "position", SetAttribute.ValueSource.Slot, RandomMode.Off, AttributeCompositionMode.Add);
                var slot = Slot(off, "position");
                if (trail) slot.space = VFXSpace.Local;
                slot.value = (Position)l.offset;
            }

            // direction is stored per particle (read by orientation / motion)
            if (l.constantDirection.HasValue)
            {
                var d = SetAttr(init, "direction");
                var slot = Slot(d, "direction");
                if (slot.spaceable) slot.space = VFXSpace.Local;
                slot.value = (DirectionType)l.constantDirection.Value;
            }
            else if (!trail) SetAttr(init, "direction", SetAttribute.ValueSource.Source);

            var life = SetAttr(init, "lifetime", SetAttribute.ValueSource.Slot, RandomMode.Uniform);
            Slot(life, "A").value = l.lifetime.x;
            Slot(life, "B").value = l.lifetime.y;

            if (l.motion != Motion.None)
            {
                if (l.motion == Motion.Direction)
                {
                    var vel = SetAttr(init, "velocity");
                    var dir = AttributeParam(graph, "direction", VFXAttributeLocation.Current, new Vector2(x - 300, 300));
                    Slot(vel, "velocity").Link(dir.outputSlots[0]);
                    var add = SetAttr(init, "velocity", SetAttribute.ValueSource.Slot, RandomMode.PerComponent, AttributeCompositionMode.Add);
                    Slot(add, "A").value = (Vector)new Vector3(-l.spread, 0, -l.spread);
                    Slot(add, "B").value = (Vector)new Vector3(l.spread, l.spread * 0.5f, l.spread);
                }
                else
                {
                    var vel = SetAttr(init, "velocity", SetAttribute.ValueSource.Slot, RandomMode.PerComponent);
                    float y0 = l.motion == Motion.Flat ? 0f : l.motion == Motion.Up ? 0.4f : -1f;
                    float y1 = l.motion == Motion.Flat ? 0f : 1f;
                    float h = l.motion == Motion.Up ? 0.35f : 1f;
                    Slot(vel, "A").value = (Vector)new Vector3(-h, y0, -h);
                    Slot(vel, "B").value = (Vector)new Vector3(h, y1, h);
                }
                var speed = SetAttr(init, "velocity", SetAttribute.ValueSource.Slot, RandomMode.Uniform, AttributeCompositionMode.Multiply);
                Slot(speed, "A").value = (Vector)(Vector3.one * l.speed.x);
                Slot(speed, "B").value = (Vector)(Vector3.one * l.speed.y);
            }

            if (l.randomRotation && l.facing != Facing.Velocity && l.facing != Facing.GroundAlongDirection)
            {
                var rot = SetAttr(init, "angleZ", SetAttribute.ValueSource.Slot, RandomMode.Uniform);
                Slot(rot, "A").value = 0f;
                Slot(rot, "B").value = 360f;
            }
            if (l.frames == Frames.Random)
            {
                var tex = SetAttr(init, "texIndex", SetAttribute.ValueSource.Slot, RandomMode.Uniform);
                Slot(tex, "A").value = 0f;
                Slot(tex, "B").value = l.cols * l.rows - 0.01f;
            }

            var update = Add<VFXBasicUpdate>(graph);
            update.position = new Vector2(x, 650);
            update.LinkFrom(init);
            if (l.drag > 0)
            {
                var drag = ScriptableObject.CreateInstance<Drag>();
                update.AddChild(drag);
                Slot(drag, "dragCoefficient").value = l.drag;
            }
            if (l.frames == Frames.Animate)
                Curve(update, "texIndex", AnimationCurve.Linear(0, 0, 1, l.cols * l.rows - 0.01f), AttributeCompositionMode.Overwrite);

            var output = Add<VFXPlanarPrimitiveOutput>(graph);
            output.position = new Vector2(x, 950);
            output.LinkFrom(update);
            output.SetSettingValue("blendMode", l.alphaBlend ? VFXAbstractRenderedOutput.BlendMode.Alpha : VFXAbstractRenderedOutput.BlendMode.Additive);
            if (l.texture != null)
            {
                bool flip = l.cols * l.rows > 1;
                if (flip)
                {
                    output.SetSettingValue("uvMode", VFXAbstractParticleOutput.UVMode.Flipbook);
                    output.SetSettingValue("flipbookBlendFrames", l.frames == Frames.Animate);
                }
                Slot(output, "mainTexture").value = Texture(l.texture);
                if (flip) Slot(output, "flipBookSize").value = new FlipBook { x = l.cols, y = l.rows };
            }

            var orient = ScriptableObject.CreateInstance<Orient>();
            switch (l.facing)
            {
                case Facing.Camera: orient.SetSettingValue("mode", Orient.Mode.FaceCameraPlane); break;
                case Facing.Velocity: orient.SetSettingValue("mode", Orient.Mode.AlongVelocity); break;
                default:
                    orient.SetSettingValue("mode", Orient.Mode.Advanced);
                    orient.SetSettingValue("axes", Orient.AxesPair.ZY);
                    break;
            }
            output.AddChild(orient);
            if (l.facing == Facing.Ground || l.facing == Facing.GroundAlongDirection)
            {
                var slots = ((IVFXSlotContainer)orient).inputSlots;
                slots[0].value = (DirectionType)Vector3.up;                  // quad normal (Z) faces the sky
                if (l.facing == Facing.Ground) slots[1].value = (DirectionType)Vector3.forward;
                else
                {
                    var dir = AttributeParam(graph, "direction", VFXAttributeLocation.Current, new Vector2(x - 300, 1000));
                    slots[1].Link(dir.outputSlots[0]);                         // quad Y follows the event direction
                }
            }

            switch (l.sizeCurve)
            {
                case SizeCurve.Shrink:
                    Curve(output, "size", new AnimationCurve(new Keyframe(0, 1, 0, 0), new Keyframe(1, 0, -1.5f, 0)));
                    break;
                case SizeCurve.Grow:
                    Curve(output, "size", new AnimationCurve(new Keyframe(0, l.growFrom, 0, 2f), new Keyframe(1, 1, 0.3f, 0)));
                    break;
                case SizeCurve.Pop:
                    Curve(output, "size", new AnimationCurve(new Keyframe(0, l.growFrom), new Keyframe(0.25f, 1f, 0.5f, 0.2f), new Keyframe(1, 1.1f)));
                    break;
            }
            Curve(output, "alpha", l.fadeIn
                ? new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.15f, 1), new Keyframe(0.6f, 0.8f), new Keyframe(1, 0))
                : new AnimationCurve(new Keyframe(0, 1), new Keyframe(0.55f, 0.85f), new Keyframe(1, 0)));
            if (l.scale != Vector2.one)
            {
                var scale = SetAttr(output, "scale");
                Slot(scale, "scale").value = new Vector3(l.scale.x, l.scale.y, 1f);
            }
        }

        public static string Build(Spec s)
        {
            string path = $"{OutDir}/VFX_{s.name}.vfx";
            System.IO.Directory.CreateDirectory(OutDir);
            if (System.IO.File.Exists(path)) AssetDatabase.DeleteAsset(path);
            VisualEffectAssetEditorUtility.CreateNewAsset(path);
            var resource = VisualEffectResource.GetResourceAtPath(path);
            var graph = resource.GetGraph() ?? resource.CreateGraph();

            VFXBasicEvent evt = null;
            if (s.layers.Any(l => l.rate <= 0))
            {
                evt = Add<VFXBasicEvent>(graph);
                evt.SetSettingValue("eventName", "OnPlay");
                evt.position = new Vector2(0, -320);
            }
            for (int i = 0; i < s.layers.Length; i++) BuildLayer(graph, evt, s.layers[i], i, s.worldSpace);

            resource.WriteAssetWithSubAssets();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return path;
        }

        // ------------------------------------------------------------------ effect definitions

        static Layer Sparks(int count, Vector2 speed, Vector2 life, Vector2 size, Motion motion = Motion.Sphere, float drag = 3f, float boost = 3f) => new Layer
        {
            texture = "Tx_flash_02.png", facing = Facing.Velocity, motion = motion, count = count, speed = speed, lifetime = life,
            sizeMul = size, sizeCurve = SizeCurve.Shrink, drag = drag, colorBoost = boost, scale = new Vector2(0.3f, 2.2f), randomRotation = false,
        };

        public static List<Spec> Specs() => new()
        {
            new Spec { name = "Hit", layers = new[]
            {
                new Layer { texture = "Tx_Burst_radial_05.png", cols = 2, rows = 2, frames = Frames.Random, lifetime = new Vector2(0.1f, 0.14f), sizeMul = new Vector2(0.9f, 1.2f), sizeCurve = SizeCurve.Pop, growFrom = 0.5f, colorBoost = 2.5f },
                new Layer { texture = "Tx_Impact_02.tga", cols = 4, rows = 1, frames = Frames.Random, facing = Facing.GroundAlongDirection, lifetime = new Vector2(0.12f, 0.18f), sizeMul = new Vector2(0.9f, 1.1f), sizeCurve = SizeCurve.Pop, colorBoost = 2f, scale = new Vector2(0.5f, 1.4f), offset = new Vector3(0, 0.2f, 0) },
                Sparks(6, new Vector2(3, 7), new Vector2(0.12f, 0.25f), new Vector2(0.12f, 0.22f)),
            }},
            new Spec { name = "Crit", layers = new[]
            {
                new Layer { texture = "Tx_Burst_radial_07.png", cols = 2, rows = 2, frames = Frames.Random, lifetime = new Vector2(0.16f, 0.2f), sizeMul = new Vector2(2.2f, 2.6f), sizeCurve = SizeCurve.Pop, growFrom = 0.4f, colorBoost = 3f },
                new Layer { texture = "Tx_flash_01.png", lifetime = new Vector2(0.1f, 0.12f), sizeMul = new Vector2(2.5f, 3f), sizeCurve = SizeCurve.Shrink, colorBoost = 2f },
                Sparks(12, new Vector2(5, 10), new Vector2(0.15f, 0.3f), new Vector2(0.15f, 0.3f)),
            }},
            new Spec { name = "Death", layers = new[]
            {
                new Layer { texture = "Tx_Smoke_radial_02.png", cols = 4, rows = 4, frames = Frames.Animate, alphaBlend = true, lifetime = new Vector2(0.55f, 0.65f), sizeMul = new Vector2(2.2f, 2.6f), sizeCurve = SizeCurve.Grow, growFrom = 0.7f, colorBoost = 1f },
                new Layer { texture = "Tx_Burst_radial_02.png", cols = 3, rows = 2, frames = Frames.Animate, lifetime = new Vector2(0.3f, 0.3f), sizeMul = new Vector2(2.4f, 2.4f), colorBoost = 2.5f },
                new Layer { texture = "Tx_flash_02.png", motion = Motion.Up, count = 10, speed = new Vector2(1.5f, 4f), lifetime = new Vector2(0.5f, 0.9f), sizeMul = new Vector2(0.25f, 0.45f), sizeCurve = SizeCurve.Shrink, drag = 2f, colorBoost = 2.5f },
            }},
            new Spec { name = "Muzzle", layers = new[]
            {
                new Layer { texture = "Tx_Burst_directional_02.png", cols = 2, rows = 2, frames = Frames.Random, facing = Facing.GroundAlongDirection, lifetime = new Vector2(0.08f, 0.11f), sizeMul = new Vector2(1.4f, 1.7f), sizeCurve = SizeCurve.Pop, growFrom = 0.4f, colorBoost = 2.5f, scale = new Vector2(0.6f, 1f), offset = Vector3.zero },
                new Layer { texture = "Tx_flash_02.png", lifetime = new Vector2(0.06f, 0.09f), sizeMul = new Vector2(1f, 1.3f), sizeCurve = SizeCurve.Shrink, colorBoost = 2.5f },
            }},
            new Spec { name = "Explosion", layers = new[]
            {
                new Layer { texture = "Tx_Fire_radial_02.png", cols = 3, rows = 4, frames = Frames.Animate, lifetime = new Vector2(0.5f, 0.5f), sizeMul = new Vector2(2.6f, 2.8f), sizeCurve = SizeCurve.Grow, growFrom = 0.6f, colorBoost = 2.5f, capacity = 256 },
                new Layer { texture = "Tx_Smoke_radial_04.png", cols = 4, rows = 4, frames = Frames.Animate, alphaBlend = true, delay = 0.12f, lifetime = new Vector2(0.8f, 0.9f), sizeMul = new Vector2(2.6f, 3f), sizeCurve = SizeCurve.Grow, growFrom = 0.6f, fixedColor = new Color(0.22f, 0.2f, 0.24f), colorBoost = 1f, offset = new Vector3(0, 0.4f, 0), capacity = 256 },
                new Layer { texture = "Tx_Ring_01.png", cols = 2, rows = 2, frames = Frames.Random, facing = Facing.Ground, lifetime = new Vector2(0.35f, 0.35f), sizeMul = new Vector2(2.2f, 2.2f), sizeCurve = SizeCurve.Grow, growFrom = 0.2f, colorBoost = 2f, offset = new Vector3(0, 0.08f, 0), capacity = 256 },
                new Layer { texture = "Tx_crack_02.PNG", alphaBlend = true, facing = Facing.Ground, lifetime = new Vector2(1f, 1.2f), sizeMul = new Vector2(0.9f, 1.1f), fixedColor = new Color(0.08f, 0.06f, 0.06f), colorBoost = 1f, offset = new Vector3(0, 0.03f, 0), capacity = 128 },
                Sparks(24, new Vector2(4, 11), new Vector2(0.3f, 0.6f), new Vector2(0.2f, 0.4f), Motion.Up, 3f),
            }},
            new Spec { name = "Pickup", layers = new[]
            {
                new Layer { texture = "Tx_Burst_radial_07.png", cols = 2, rows = 2, frames = Frames.Random, lifetime = new Vector2(0.18f, 0.22f), sizeMul = new Vector2(1.2f, 1.5f), sizeCurve = SizeCurve.Pop, growFrom = 0.3f, colorBoost = 2.5f },
                new Layer { texture = "Tx_flash_02.png", motion = Motion.Up, count = 5, speed = new Vector2(1, 2.5f), lifetime = new Vector2(0.25f, 0.4f), sizeMul = new Vector2(0.3f, 0.5f), sizeCurve = SizeCurve.Shrink, colorBoost = 2.5f },
            }},
            new Spec { name = "LevelUp", layers = new[]
            {
                new Layer { texture = "Tx_Ring_01.png", cols = 2, rows = 2, frames = Frames.Animate, facing = Facing.Ground, lifetime = new Vector2(0.7f, 0.7f), sizeMul = new Vector2(4f, 4f), sizeCurve = SizeCurve.Grow, growFrom = 0.2f, colorBoost = 3f, offset = new Vector3(0, 0.08f, 0) },
                new Layer { texture = "Tx_Burst_radial_07.png", cols = 2, rows = 2, frames = Frames.Animate, lifetime = new Vector2(0.45f, 0.45f), sizeMul = new Vector2(3.5f, 3.5f), sizeCurve = SizeCurve.Pop, growFrom = 0.3f, colorBoost = 3f, offset = new Vector3(0, 1f, 0) },
                new Layer { texture = "Tx_flash_02.png", motion = Motion.Up, count = 40, speed = new Vector2(2, 5), lifetime = new Vector2(0.8f, 1.3f), sizeMul = new Vector2(0.2f, 0.35f), sizeCurve = SizeCurve.Shrink, drag = 1f, colorBoost = 3f },
            }},
            new Spec { name = "Slash", layers = new[]
            {
                // Crescent swoosh flipbook: wide axis lies across the swing direction.
                new Layer { texture = "Tx_Fire_directional_12.png", cols = 3, rows = 2, frames = Frames.Animate, facing = Facing.GroundAlongDirection, lifetime = new Vector2(0.2f, 0.2f), sizeMul = new Vector2(1.3f, 1.3f), colorBoost = 2.5f, scale = new Vector2(1f, 0.37f) },
                Sparks(10, new Vector2(5, 10), new Vector2(0.12f, 0.22f), new Vector2(0.08f, 0.14f), Motion.Flat, 8f),
            }},
            new Spec { name = "SpawnWarning", layers = new[]
            {
                new Layer { texture = "Tx_Crack_02_dissolve.png", facing = Facing.Ground, lifetime = new Vector2(0.6f, 0.7f), sizeMul = new Vector2(1.6f, 1.8f), sizeCurve = SizeCurve.Grow, growFrom = 0.4f, colorBoost = 2f, fadeIn = true, offset = new Vector3(0, 0.05f, 0) },
                new Layer { texture = "Tx_Smoke_directional_04.png", cols = 3, rows = 3, frames = Frames.Animate, alphaBlend = true, lifetime = new Vector2(0.6f, 0.6f), sizeMul = new Vector2(1.6f, 1.8f), colorBoost = 0.6f, randomRotation = false, offset = new Vector3(0, 0.6f, 0) },
            }},
            new Spec { name = "Nova", layers = new[]
            {
                new Layer { texture = "Tx_Ring_01.png", cols = 2, rows = 2, frames = Frames.Animate, facing = Facing.Ground, lifetime = new Vector2(0.4f, 0.4f), sizeMul = new Vector2(2.2f, 2.2f), sizeCurve = SizeCurve.Grow, growFrom = 0.15f, colorBoost = 3f },
                new Layer { texture = "Tx_Burst_radial_02.png", cols = 3, rows = 2, frames = Frames.Animate, facing = Facing.Ground, lifetime = new Vector2(0.35f, 0.35f), sizeMul = new Vector2(1.8f, 1.8f), sizeCurve = SizeCurve.Grow, growFrom = 0.4f, colorBoost = 2f },
                new Layer { texture = "Tx_flash_02.png", facing = Facing.Velocity, motion = Motion.Flat, count = 36, speed = new Vector2(9, 11), lifetime = new Vector2(0.3f, 0.36f), sizeMul = new Vector2(0.12f, 0.2f), colorBoost = 3f, scale = new Vector2(0.3f, 2.4f), randomRotation = false },
            }},
            new Spec { name = "Lightning", layers = new[]
            {
                // size = segment length; quad lies flat along the bolt (direction).
                new Layer { texture = "Tx_electricity_05.png", cols = 4, rows = 1, frames = Frames.Animate, facing = Facing.GroundAlongDirection, lifetime = new Vector2(0.18f, 0.22f), sizeMul = new Vector2(1f, 1f), colorBoost = 3f, scale = new Vector2(0.35f, 1f) },
                new Layer { texture = "Tx_electricity_separate_01.png", cols = 4, rows = 4, frames = Frames.Random, facing = Facing.GroundAlongDirection, lifetime = new Vector2(0.12f, 0.16f), sizeMul = new Vector2(1f, 1.1f), colorBoost = 2.5f, scale = new Vector2(0.25f, 1f), delay = 0.05f },
            }},
            new Spec { name = "Dash", layers = new[]
            {
                new Layer { texture = "Tx_Smoke_directional_05.png", cols = 4, rows = 2, frames = Frames.Animate, alphaBlend = true, lifetime = new Vector2(0.45f, 0.5f), sizeMul = new Vector2(2f, 2.2f), colorBoost = 0.9f, randomRotation = false, offset = new Vector3(0, 0.6f, 0) },
                new Layer { texture = "Tx_Wind_02.png", facing = Facing.GroundAlongDirection, lifetime = new Vector2(0.25f, 0.3f), sizeMul = new Vector2(2.2f, 2.4f), colorBoost = 1.5f, scale = new Vector2(0.6f, 1.6f), offset = new Vector3(0, 0.15f, 0) },
                Sparks(8, new Vector2(2, 4), new Vector2(0.2f, 0.35f), new Vector2(0.15f, 0.25f), Motion.Flat, 4f, 2.5f),
            }},
            new Spec { name = "Burn", layers = new[]
            {
                new Layer { texture = "Tx_Fire_ground_01.png", cols = 4, rows = 3, frames = Frames.Animate, lifetime = new Vector2(0.45f, 0.5f), sizeMul = new Vector2(1.1f, 1.3f), colorBoost = 2.5f, randomRotation = false },
                new Layer { texture = "Tx_flash_02.png", motion = Motion.Up, count = 3, speed = new Vector2(1, 2), lifetime = new Vector2(0.3f, 0.5f), sizeMul = new Vector2(0.15f, 0.25f), sizeCurve = SizeCurve.Shrink, colorBoost = 3f },
            }},
            new Spec { name = "Frost", layers = new[]
            {
                new Layer { texture = "Tx_Burst_radial_06.png", cols = 2, rows = 2, frames = Frames.Random, lifetime = new Vector2(0.25f, 0.3f), sizeMul = new Vector2(1f, 1.2f), sizeCurve = SizeCurve.Pop, growFrom = 0.3f, colorBoost = 2.2f },
                new Layer { texture = "Tx_Spike_01.png", cols = 2, rows = 2, frames = Frames.Random, facing = Facing.Ground, lifetime = new Vector2(0.5f, 0.6f), sizeMul = new Vector2(1.2f, 1.5f), sizeCurve = SizeCurve.Pop, colorBoost = 2f, offset = new Vector3(0, -0.9f, 0) },
            }},
            new Spec { name = "Poison", layers = new[]
            {
                new Layer { texture = "Tx_Smoke_08.png", cols = 2, rows = 2, frames = Frames.Random, alphaBlend = true, motion = Motion.Up, count = 2, speed = new Vector2(0.5f, 1.2f), lifetime = new Vector2(0.6f, 0.8f), sizeMul = new Vector2(0.8f, 1.1f), sizeCurve = SizeCurve.Grow, growFrom = 0.4f, colorBoost = 1.2f, fadeIn = true },
                new Layer { texture = "Tx_Liquid_01.png", lifetime = new Vector2(0.25f, 0.3f), sizeMul = new Vector2(0.7f, 0.9f), sizeCurve = SizeCurve.Pop, colorBoost = 1.5f },
            }},
            new Spec { name = "PlayerHurt", layers = new[]
            {
                new Layer { texture = "Tx_Impact_02.png", cols = 2, rows = 2, frames = Frames.Random, lifetime = new Vector2(0.2f, 0.24f), sizeMul = new Vector2(2f, 2.3f), sizeCurve = SizeCurve.Pop, growFrom = 0.4f, colorBoost = 2.5f },
                new Layer { texture = "Tx_Burst_radial_03.png", cols = 2, rows = 2, frames = Frames.Random, facing = Facing.Ground, lifetime = new Vector2(0.25f, 0.3f), sizeMul = new Vector2(2.5f, 2.8f), sizeCurve = SizeCurve.Grow, growFrom = 0.3f, colorBoost = 2f, offset = new Vector3(0, -0.9f, 0) },
            }},
            new Spec { name = "Heal", layers = new[]
            {
                new Layer { texture = "Tx_Ring_01.png", cols = 2, rows = 2, frames = Frames.Random, facing = Facing.Ground, lifetime = new Vector2(0.5f, 0.5f), sizeMul = new Vector2(2.4f, 2.4f), sizeCurve = SizeCurve.Grow, growFrom = 0.3f, colorBoost = 2f, offset = new Vector3(0, 0.08f, 0) },
                new Layer { texture = "Tx_cross_05.png", motion = Motion.Up, count = 8, speed = new Vector2(1, 2.5f), lifetime = new Vector2(0.6f, 0.9f), sizeMul = new Vector2(0.25f, 0.4f), sizeCurve = SizeCurve.Shrink, colorBoost = 2.5f, randomRotation = false, offset = new Vector3(0, 0.5f, 0) },
            }},
            new Spec { name = "Spawn", layers = new[]
            {
                new Layer { texture = "Tx_Smoke_radial_03.png", cols = 4, rows = 4, frames = Frames.Animate, alphaBlend = true, lifetime = new Vector2(0.6f, 0.7f), sizeMul = new Vector2(2.2f, 2.5f), colorBoost = 1f, offset = new Vector3(0, 0.7f, 0) },
                new Layer { texture = "Tx_Burst_radial_01.png", cols = 2, rows = 2, frames = Frames.Animate, facing = Facing.Ground, lifetime = new Vector2(0.3f, 0.3f), sizeMul = new Vector2(2f, 2f), colorBoost = 2f, offset = new Vector3(0, 0.06f, 0) },
            }},
            new Spec { name = "EnemyShoot", layers = new[]
            {
                new Layer { texture = "Tx_Burst_radial_05.png", cols = 2, rows = 2, frames = Frames.Random, lifetime = new Vector2(0.12f, 0.15f), sizeMul = new Vector2(1.2f, 1.4f), sizeCurve = SizeCurve.Pop, growFrom = 0.3f, colorBoost = 2.5f },
            }},
            // ---- weapon hit impacts (size = 1 normal hit, ~1.4 crit; scaled by enemy size)
            new Spec { name = "ImpactArcane", layers = new[]
            {
                new Layer { texture = "Tx_Burst_radial_01.png", cols = 2, rows = 2, frames = Frames.Animate, lifetime = new Vector2(0.18f, 0.2f), sizeMul = new Vector2(1.3f, 1.5f), sizeCurve = SizeCurve.Pop, growFrom = 0.4f, colorBoost = 2.5f },
                new Layer { texture = "Tx_flash_02.png", lifetime = new Vector2(0.08f, 0.1f), sizeMul = new Vector2(1.2f, 1.4f), sizeCurve = SizeCurve.Shrink, colorBoost = 2.5f },
                Sparks(5, new Vector2(3, 6), new Vector2(0.12f, 0.22f), new Vector2(0.1f, 0.18f)),
            }},
            new Spec { name = "ImpactFire", layers = new[]
            {
                new Layer { texture = "Tx_Fire_radial_03.png", cols = 3, rows = 3, frames = Frames.Animate, lifetime = new Vector2(0.3f, 0.35f), sizeMul = new Vector2(1.4f, 1.7f), sizeCurve = SizeCurve.Grow, growFrom = 0.5f, colorBoost = 2.5f },
                new Layer { texture = "Tx_flash_02.png", motion = Motion.Up, count = 6, speed = new Vector2(2, 4), lifetime = new Vector2(0.25f, 0.45f), sizeMul = new Vector2(0.12f, 0.22f), sizeCurve = SizeCurve.Shrink, drag = 2f, colorBoost = 3f },
            }},
            new Spec { name = "ImpactFrost", layers = new[]
            {
                new Layer { texture = "Tx_Burst_radial_06.png", cols = 2, rows = 2, frames = Frames.Random, lifetime = new Vector2(0.18f, 0.22f), sizeMul = new Vector2(1.3f, 1.5f), sizeCurve = SizeCurve.Pop, growFrom = 0.3f, colorBoost = 2.5f },
                new Layer { texture = "Tx_Spike_01.png", cols = 2, rows = 2, frames = Frames.Random, count = 2, lifetime = new Vector2(0.25f, 0.3f), sizeMul = new Vector2(0.9f, 1.2f), sizeCurve = SizeCurve.Pop, growFrom = 0.5f, colorBoost = 2f },
                Sparks(6, new Vector2(2, 5), new Vector2(0.15f, 0.3f), new Vector2(0.1f, 0.18f), Motion.Sphere, 3f, 2.5f),
            }},
            new Spec { name = "ImpactPoison", layers = new[]
            {
                new Layer { texture = "Tx_Liquid_01.png", lifetime = new Vector2(0.22f, 0.26f), sizeMul = new Vector2(1.2f, 1.4f), sizeCurve = SizeCurve.Pop, growFrom = 0.3f, colorBoost = 1.8f },
                new Layer { texture = "Tx_Smoke_08.png", cols = 2, rows = 2, frames = Frames.Random, alphaBlend = true, motion = Motion.Up, count = 2, speed = new Vector2(0.4f, 1f), lifetime = new Vector2(0.4f, 0.55f), sizeMul = new Vector2(0.7f, 0.9f), sizeCurve = SizeCurve.Grow, growFrom = 0.4f, colorBoost = 1.1f, fadeIn = true },
            }},
            new Spec { name = "ImpactSlash", layers = new[]
            {
                new Layer { texture = "Tx_Burst_directional_01.png", cols = 2, rows = 2, frames = Frames.Random, facing = Facing.GroundAlongDirection, lifetime = new Vector2(0.14f, 0.18f), sizeMul = new Vector2(1.6f, 1.9f), sizeCurve = SizeCurve.Pop, growFrom = 0.5f, colorBoost = 2.5f, scale = new Vector2(1f, 0.5f) },
                new Layer { texture = "Tx_Impact_02.tga", cols = 4, rows = 1, frames = Frames.Random, lifetime = new Vector2(0.1f, 0.14f), sizeMul = new Vector2(1.1f, 1.3f), sizeCurve = SizeCurve.Pop, colorBoost = 2.5f, scale = new Vector2(0.4f, 1.2f) },
                Sparks(7, new Vector2(4, 8), new Vector2(0.12f, 0.2f), new Vector2(0.1f, 0.16f), Motion.Direction, 5f),
            }},
            new Spec { name = "ImpactShock", layers = new[]
            {
                new Layer { texture = "Tx_electricity_01.png", cols = 4, rows = 4, frames = Frames.Animate, lifetime = new Vector2(0.22f, 0.26f), sizeMul = new Vector2(1.5f, 1.8f), colorBoost = 3f },
                new Layer { texture = "Tx_flash_01.png", lifetime = new Vector2(0.08f, 0.1f), sizeMul = new Vector2(1.5f, 1.8f), sizeCurve = SizeCurve.Shrink, colorBoost = 2f },
            }},
            new Spec { name = "ImpactHoly", layers = new[]
            {
                new Layer { texture = "Tx_Burst_radial_07.png", cols = 2, rows = 2, frames = Frames.Random, lifetime = new Vector2(0.2f, 0.24f), sizeMul = new Vector2(1.6f, 1.9f), sizeCurve = SizeCurve.Pop, growFrom = 0.3f, colorBoost = 2.8f },
                new Layer { texture = "Tx_flash_01.png", lifetime = new Vector2(0.12f, 0.15f), sizeMul = new Vector2(1.6f, 2f), sizeCurve = SizeCurve.Shrink, colorBoost = 1.8f },
            }},
            new Spec { name = "ImpactBlunt", layers = new[]
            {
                new Layer { texture = "Tx_Burst_radial_02.png", cols = 3, rows = 2, frames = Frames.Animate, lifetime = new Vector2(0.22f, 0.25f), sizeMul = new Vector2(1.6f, 1.9f), sizeCurve = SizeCurve.Grow, growFrom = 0.5f, colorBoost = 2.5f },
                new Layer { texture = "Tx_Smoke_radial_01.png", cols = 3, rows = 3, frames = Frames.Animate, alphaBlend = true, lifetime = new Vector2(0.4f, 0.45f), sizeMul = new Vector2(1.3f, 1.5f), fixedColor = new Color(0.55f, 0.5f, 0.45f), colorBoost = 1f, offset = new Vector3(0, -0.5f, 0) },
                Sparks(6, new Vector2(3, 6), new Vector2(0.15f, 0.25f), new Vector2(0.12f, 0.2f), Motion.Up, 3f),
            }},
            // ---- trails (continuous, placed on projectile prefabs)
            new Spec { name = "Trail_Fire", worldSpace = true, layers = new[]
            {
                new Layer { rate = 28, tint = new Color(1f, 0.45f, 0.12f), texture = "Tx_Fire_radial_03.png", cols = 3, rows = 3, frames = Frames.Animate, lifetime = new Vector2(0.25f, 0.35f), sizeMul = new Vector2(0.6f, 0.8f), sizeCurve = SizeCurve.Shrink, colorBoost = 2.5f, capacity = 2048 },
                new Layer { rate = 10, tint = new Color(0.5f, 0.45f, 0.5f), texture = "Tx_Smoke_tex_01.png", cols = 2, rows = 2, frames = Frames.Random, alphaBlend = true, motion = Motion.Up, speed = new Vector2(0.3f, 0.8f), lifetime = new Vector2(0.4f, 0.6f), sizeMul = new Vector2(0.5f, 0.7f), sizeCurve = SizeCurve.Grow, colorBoost = 0.35f, fadeIn = true, capacity = 1024 },
            }},
            new Spec { name = "Trail_Frost", worldSpace = true, layers = new[]
            {
                new Layer { rate = 24, tint = new Color(0.45f, 0.8f, 1f), texture = "Tx_flash_02.png", lifetime = new Vector2(0.25f, 0.35f), sizeMul = new Vector2(0.35f, 0.5f), sizeCurve = SizeCurve.Shrink, colorBoost = 2f, capacity = 1024 },
                new Layer { rate = 8, tint = new Color(0.6f, 0.9f, 1f), texture = "Tx_Burst_radial_06.png", cols = 2, rows = 2, frames = Frames.Random, lifetime = new Vector2(0.25f, 0.3f), sizeMul = new Vector2(0.4f, 0.55f), sizeCurve = SizeCurve.Shrink, colorBoost = 1.5f, capacity = 512 },
            }},
            new Spec { name = "Trail_Poison", worldSpace = true, layers = new[]
            {
                new Layer { rate = 14, tint = new Color(0.45f, 1f, 0.3f), texture = "Tx_Smoke_08.png", cols = 2, rows = 2, frames = Frames.Random, alphaBlend = true, lifetime = new Vector2(0.35f, 0.5f), sizeMul = new Vector2(0.45f, 0.65f), sizeCurve = SizeCurve.Grow, colorBoost = 1f, fadeIn = true, capacity = 1024 },
            }},
            new Spec { name = "Trail_Arcane", worldSpace = true, layers = new[]
            {
                new Layer { rate = 30, tint = new Color(0.7f, 0.5f, 1f), texture = "Tx_flash_02.png", lifetime = new Vector2(0.15f, 0.22f), sizeMul = new Vector2(0.3f, 0.45f), sizeCurve = SizeCurve.Shrink, colorBoost = 2f, capacity = 4096 },
            }},
            new Spec { name = "Trail_Enemy", worldSpace = true, layers = new[]
            {
                new Layer { rate = 20, tint = new Color(1f, 0.3f, 0.85f), texture = "Tx_flash_02.png", lifetime = new Vector2(0.2f, 0.3f), sizeMul = new Vector2(0.45f, 0.6f), sizeCurve = SizeCurve.Shrink, colorBoost = 2f, capacity = 2048 },
            }},
        };

        [MenuItem("Tools/VFX/Generate VFX Graphs")]
        public static void GenerateAll() => Debug.Log(Run());

        public static string Run()
        {
            var results = new List<string>();
            foreach (var s in Specs())
            {
                try { results.Add("OK " + Build(s)); }
                catch (Exception e) { results.Add("FAIL " + s.name + ": " + e.Message); }
            }
            AssetDatabase.SaveAssets();
            return string.Join(Environment.NewLine, results);
        }
    }
}

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace IconTools.Editor
{
    /// <summary>
    /// Renders 3D models (prefabs) into transparent, outlined PNG sprites in an isolated preview scene (URP).
    /// The image size follows a cell footprint (<see cref="Options.pixelsPerCell"/> per cell), so icons for grid
    /// inventories line up with their slots. Long models (swords, staffs) lie along the slot's long axis - diagonally
    /// in square slots; compact props get the pose whose silhouette fills the slot best; a compact model in a long
    /// slot is tiled (e.g. three gems in a 1x3 slot).
    ///
    /// Use from code: <c>ModelIconRenderer.RenderBatch(new[] { new IconJob(prefab, new Vector2Int(1, 3), "Assets/Icons/Staff.png") })</c>
    /// or select prefabs and run Tools/Icons/Render Selected Prefabs (1x1).
    /// </summary>
    public static class ModelIconRenderer
    {
        public class Options
        {
            public int pixelsPerCell = 128;
            public int supersample = 2;           // render at 2x and downsample for clean edges
            public float padding = 0.1f;          // fraction of the image kept free around the model
            public int outlinePx = 3;             // 0 = no outline
            public Color outlineColor = new Color(0.09f, 0.06f, 0.05f, 1f);
            /// <summary>Most model packs face +Z; the camera looks along +Z, so models are turned 180° to show their front.</summary>
            public Vector3 facingCorrection = new Vector3(0, 180f, 0);
            public bool searchOrientation = true; // pixel-coverage search for compact models
            public bool tileCompactInLongSlots = true;
        }

        public readonly struct IconJob
        {
            public readonly GameObject model;
            public readonly Vector2Int cells;
            public readonly string pngPath;
            /// <summary>Non-zero = fixed pose (Euler, applied before the facing correction); skips the automatic pose.</summary>
            public readonly Vector3 rotationOverride;

            public IconJob(GameObject model, Vector2Int cells, string pngPath, Vector3 rotationOverride = default)
            {
                this.model = model; this.cells = cells; this.pngPath = pngPath; this.rotationOverride = rotationOverride;
            }
        }

        [MenuItem("Tools/Icons/Render Selected Prefabs")]
        private static void RenderSelected()
        {
            var jobs = Selection.gameObjects
                .Where(g => PrefabUtility.IsPartOfPrefabAsset(g))
                .Select(g =>
                {
                    var dir = Path.Combine(Path.GetDirectoryName(AssetDatabase.GetAssetPath(g)) ?? "Assets", "Icons");
                    return new IconJob(g, Vector2Int.one, Path.Combine(dir, g.name + ".png").Replace('\\', '/'));
                }).ToArray();
            Debug.Log($"Rendered {RenderBatch(jobs)} icons.");
        }

        /// <summary>Renders every job, imports the PNGs as sprites, returns the count rendered.</summary>
        public static int RenderBatch(IconJob[] jobs, Options options = null)
        {
            options ??= new Options();
            int rendered = 0;
            var scene = EditorSceneManager.NewPreviewScene();
            var probe = new Probe();
            try
            {
                var rig = BuildRig(scene, out var cam);
                foreach (var job in jobs)
                {
                    if (job.model == null) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(job.pngPath)!);
                    RenderOne(scene, cam, job, options, probe);
                    rendered++;
                }
                Object.DestroyImmediate(rig);
            }
            finally
            {
                probe.Dispose();
                EditorSceneManager.ClosePreviewScene(scene);
            }

            AssetDatabase.Refresh();
            foreach (var job in jobs) ImportAsSprite(job.pngPath);
            return rendered;
        }

        private static void ImportAsSprite(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter ti) return;
            if (ti.textureType == TextureImporterType.Sprite && ti.alphaIsTransparency && !ti.mipmapEnabled) return;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
        }

        // ------------------------------------------------------------------ scene

        private static GameObject BuildRig(Scene scene, out Camera cam)
        {
            var rig = new GameObject("IconRig");
            SceneManager.MoveGameObjectToScene(rig, scene);
            var camGo = new GameObject("IconCamera");
            camGo.transform.SetParent(rig.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.scene = scene; // render only the preview scene
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.01f;
            cam.enabled = false;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false; // post FX would write alpha = 1 everywhere
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = false;
            camGo.transform.SetPositionAndRotation(new Vector3(0, 0, -10f), Quaternion.identity); // looks along +Z

            // Preview scenes get no environment lighting: key + cool fill + rim stand in for ambient.
            AddLight(rig, "Key", 1.6f, new Color(1f, 0.96f, 0.9f), new Vector3(35f, 35f, 0f));
            AddLight(rig, "Fill", 0.9f, new Color(0.75f, 0.8f, 1f), new Vector3(-10f, -30f, 0f));
            AddLight(rig, "Rim", 0.8f, new Color(0.8f, 0.7f, 1f), new Vector3(20f, 180f, 0f));
            return rig;
        }

        private static void AddLight(GameObject rig, string name, float intensity, Color color, Vector3 euler)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(rig.transform, false);
            l.type = LightType.Directional;
            l.intensity = intensity;
            l.color = color;
            l.shadows = LightShadows.None;
            l.transform.rotation = Quaternion.Euler(euler);
        }

        private static void RenderOne(Scene scene, Camera cam, IconJob job, Options o, Probe probe)
        {
            var size = new Vector2Int(Mathf.Max(1, job.cells.x), Mathf.Max(1, job.cells.y));
            int w = size.x * o.pixelsPerCell, h = size.y * o.pixelsPerCell;
            var facing = Quaternion.Euler(o.facingCorrection);

            var go = (GameObject)Object.Instantiate(job.model);
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) r.enabled = false;

            if (job.rotationOverride != Vector3.zero) go.transform.rotation = Quaternion.Euler(job.rotationOverride) * facing;
            else
            {
                bool compact = IsCompact(go);
                go.transform.rotation = BestOrientation(cam, go, size, facing, compact && o.searchOrientation, o, probe, out float coverage);
                int longCells = Mathf.Max(size.x, size.y), shortCells = Mathf.Min(size.x, size.y);
                if (o.tileCompactInLongSlots && compact && coverage < 0.3f && longCells >= 3 * shortCells)
                    TileCopies(go, size, longCells / shortCells);
            }

            Frame(cam, go, w, h, o.padding);
            var pixels = RenderPixels(cam, w * o.supersample, h * o.supersample);
            Object.DestroyImmediate(go);

            pixels = Downsample(pixels, w * o.supersample, h * o.supersample, o.supersample);
            if (o.outlinePx > 0) pixels = Outline(pixels, w, h, o.outlinePx, o.outlineColor);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(job.pngPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private static Color[] RenderPixels(Camera cam, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 1;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return px;
        }

        /// <summary>Fits the model's bounds into a w×h image (minus padding).</summary>
        private static void Frame(Camera cam, GameObject go, int w, int h, float padding)
        {
            var b = Bounds(go);
            float aspect = (float)w / h;
            float halfH = Mathf.Max(b.extents.y, b.extents.x / aspect) / (1f - 2f * padding);
            cam.orthographicSize = Mathf.Max(0.01f, halfH);
            cam.aspect = aspect;
            cam.transform.position = new Vector3(b.center.x, b.center.y, b.min.z - 5f);
            cam.farClipPlane = b.size.z + 10f;
        }

        // ------------------------------------------------------------------ orientation

        private sealed class Probe : System.IDisposable
        {
            public RenderTexture rt;
            public Texture2D read;
            public void Dispose()
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (read != null) Object.DestroyImmediate(read);
            }
        }

        private const int ProbeCell = 24;

        /// <summary>
        /// Starts from the automatic pose, then (for compact models) sweeps yaw/pitch/roll and keeps the view whose
        /// rendered silhouette covers the most of the slot - pixel coverage, so thin diagonals don't win on bounds alone.
        /// </summary>
        private static Quaternion BestOrientation(Camera cam, GameObject go, Vector2Int shape, Quaternion facing, bool search,
            Options o, Probe probe, out float bestCoverage)
        {
            go.transform.rotation = facing;
            var auto = AutoOrientation(go, shape) * facing;
            var best = auto;
            bestCoverage = Coverage(cam, go, auto, shape, o.padding, probe) * 1.1f; // favour the automatic pose
            if (!search) return auto;
            foreach (float roll in new[] { 0f, 45f, 90f, 135f })
            foreach (float pitch in new[] { -50f, -25f, 0f }) // negative = looking down onto the top
            for (float yaw = -90f; yaw <= 90f; yaw += 30f)    // front-ish views only
            {
                var q = Quaternion.Euler(0, 0, roll) * Quaternion.Euler(pitch, 0, 0) * Quaternion.Euler(0, yaw, 0) * facing;
                float c = Coverage(cam, go, q, shape, o.padding, probe);
                if (pitch == 0f) c *= 0.93f; // dead-on views look flat
                if (c > bestCoverage) { bestCoverage = c; best = q; }
            }
            bestCoverage = Mathf.Min(1f, bestCoverage);
            return best;
        }

        private static float Coverage(Camera cam, GameObject go, Quaternion q, Vector2Int shape, float padding, Probe p)
        {
            go.transform.rotation = q;
            int w = shape.x * ProbeCell, h = shape.y * ProbeCell;
            Frame(cam, go, w, h, padding);
            if (p.rt == null || p.rt.width != w || p.rt.height != h)
            {
                if (p.rt != null) RenderTexture.ReleaseTemporary(p.rt);
                p.rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                if (p.read != null) Object.DestroyImmediate(p.read);
                p.read = new Texture2D(w, h, TextureFormat.RGBA32, false);
            }
            cam.targetTexture = p.rt;
            cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = p.rt;
            p.read.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            int filled = 0;
            var px = p.read.GetPixels32();
            foreach (var c in px) if (c.a > 64) filled++;
            return filled / (float)px.Length;
        }

        /// <summary>
        /// Long models: longest axis along the image's long axis (diagonal in square slots), spun 25° so the side faces
        /// the camera. Compact models: upright 3/4 view tipped toward the camera.
        /// </summary>
        private static Quaternion AutoOrientation(GameObject go, Vector2Int shape)
        {
            var e = Bounds(go).size;
            float longest = Mathf.Max(e.x, e.y, e.z);
            float shortest = Mathf.Max(0.0001f, Mathf.Min(e.x, e.y, e.z));
            float middle = e.x + e.y + e.z - longest - shortest;
            if (longest / Mathf.Max(0.0001f, middle) <= 1.6f)
                return Quaternion.Euler(-20f, 0, 0) * Quaternion.Euler(0, 30f, 0);

            Vector3 axis = e.x == longest ? Vector3.right : e.y == longest ? Vector3.up : Vector3.forward;
            Vector3 target = shape.y > shape.x ? Vector3.up
                : shape.x > shape.y ? Vector3.right
                : new Vector3(1f, 1f, 0f).normalized;
            return Quaternion.AngleAxis(25f, target) * Quaternion.FromToRotation(axis, target);
        }

        /// <summary>True when no axis is much longer than the next (gems, pots, books - not swords or staffs).</summary>
        private static bool IsCompact(GameObject go)
        {
            var e = Bounds(go).size;
            float longest = Mathf.Max(e.x, e.y, e.z), shortest = Mathf.Min(e.x, e.y, e.z);
            float middle = e.x + e.y + e.z - longest - shortest;
            return longest / Mathf.Max(0.0001f, middle) < 1.8f;
        }

        private static void TileCopies(GameObject go, Vector2Int shape, int count)
        {
            bool horizontal = shape.x > shape.y;
            var b = Bounds(go);
            float step = (horizontal ? b.size.x : b.size.y) * 0.95f;
            var holder = new GameObject("Copies").transform;
            holder.SetParent(go.transform, false);
            for (int i = 1; i < count; i++)
            {
                var copy = Object.Instantiate(go);
                var nested = copy.transform.Find("Copies");
                if (nested != null) Object.DestroyImmediate(nested.gameObject);
                copy.transform.SetPositionAndRotation(go.transform.position + (horizontal ? Vector3.right : Vector3.down) * step * i,
                    go.transform.rotation);
                copy.transform.SetParent(holder, true);
                copy.transform.Rotate(0, 0, (i % 2 == 0 ? 1 : -1) * 12f, Space.World); // vary so copies don't look stamped
                copy.transform.localScale *= 1f - 0.12f * i;
            }
        }

        private static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------------ pixels

        /// <summary>Box downsample with premultiplied alpha (no dark fringes on transparent edges).</summary>
        private static Color[] Downsample(Color[] src, int sw, int sh, int f)
        {
            if (f <= 1) return src;
            int w = sw / f, h = sh / f;
            var dst = new Color[w * h];
            float inv = 1f / (f * f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color acc = default;
                float a = 0;
                for (int j = 0; j < f; j++)
                for (int i = 0; i < f; i++)
                {
                    var c = src[(y * f + j) * sw + x * f + i];
                    acc += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                    a += c.a;
                }
                dst[y * w + x] = a > 0 ? new Color(acc.r / a, acc.g / a, acc.b / a, a * inv) : new Color(0, 0, 0, 0);
            }
            return dst;
        }

        /// <summary>Dark rim around the silhouette (circular alpha dilation) so icons read on any background.</summary>
        private static Color[] Outline(Color[] src, int w, int h, int radius, Color outline)
        {
            var dst = (Color[])src.Clone();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = src[y * w + x];
                if (c.a >= 0.99f) continue;
                float m = 0;
                for (int j = -radius; j <= radius && m < 1f; j++)
                for (int i = -radius; i <= radius; i++)
                {
                    if (i * i + j * j > radius * radius) continue;
                    int xx = x + i, yy = y + j;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    m = Mathf.Max(m, src[yy * w + xx].a);
                }
                if (m <= 0) continue;
                float a = c.a + m * (1f - c.a);
                var rgb = (new Color(c.r, c.g, c.b) * c.a + outline * m * (1f - c.a)) / Mathf.Max(0.0001f, a);
                dst[y * w + x] = new Color(rgb.r, rgb.g, rgb.b, a);
            }
            return dst;
        }
    }
}

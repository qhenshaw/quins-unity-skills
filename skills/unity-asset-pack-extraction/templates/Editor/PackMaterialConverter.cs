using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AssetPackTools.Editor
{
    /// <summary>
    /// Converts every .mat under <see cref="PackRoot"/> to the project's own shader (default: URP Lit) when the pack's
    /// shaders were not imported. A material keeps its saved properties after a shader swap (and when its shader is
    /// missing), so the source texture slots can still be read - which also makes this safe to re-run after
    /// extracting more assets.
    ///
    /// Adapt per vendor: fill the *Slots arrays with the pack shader's property names (open a .mat in a text editor,
    /// look under m_SavedProperties.m_TexEnvs / m_Floats), and the Target* names with your shader's.
    /// Menu: Tools/Asset Packs/Convert Pack Materials.
    /// </summary>
    public static class PackMaterialConverter
    {
        // ------------------------------------------------------------------ configure
        private const string PackRoot = "Assets/ThirdParty";              // where extracted packs live
        private const string TargetShader = "Universal Render Pipeline/Lit";
        private const string TargetBaseMap = "_BaseMap", TargetBaseColor = "_BaseColor";
        private const string TargetNormalMap = "_BumpMap", TargetEmissionMap = "_EmissionMap", TargetEmissionColor = "_EmissionColor";
        private const string TargetCutoff = "_Cutoff", TargetCull = "_Cull", TargetSmoothness = "_Smoothness";

        // First non-null slot wins. These are Synty's (POLYGON / Sidekick / nature) names plus Unity's standard ones.
        private static readonly string[] BaseSlots =
        {
            "_BaseMap", "_MainTex", "_ColorMap", "_Albedo_Map", "_Base_Texture", "_Texture_Map", "_Base_Albedo",
            "_Leaf_Texture", "_Triplanar_Texture_Top", "_Trunk_Texture",
        };
        private static readonly string[] NormalSlots = { "_BumpMap", "_NormalMap", "_Normal_Map", "_Base_Normal", "_Leaf_Normal" };
        private static readonly string[] EmissionSlots = { "_EmissionMap", "_Emission_Map", "_Emission_Texture" };
        private static readonly string[] AlphaClipFlags = { "_AlphaClip", "_BUILTIN_AlphaClip", "_AlphaCutoffEnable" };
        private static readonly string[] CutoffFloats = { "_Cutoff", "_Alpha_Clip_Threshold", "_AlphaCutoff" };

        /// <summary>
        /// Low-poly packs often colour models from a tiny palette texture (e.g. Synty Sidekick 32x32 "ColorMap"s).
        /// Bilinear filtering and mipmaps blend palette cells and turn the model grey at distance, so these import
        /// point-sampled, uncompressed, without mipmaps. Search filter for AssetDatabase.FindAssets; empty = skip.
        /// </summary>
        private const string PaletteTextureFilter = "t:Texture2D ColorMap";
        private const int PaletteMaxSize = 64;

        // ------------------------------------------------------------------ run

        [MenuItem("Tools/Asset Packs/Convert Pack Materials")]
        public static void ConvertAllMenu() => Debug.Log(ConvertAll());

        public static string ConvertAll()
        {
            var shader = Shader.Find(TargetShader);
            if (shader == null) return $"Shader '{TargetShader}' not found.";
            int converted = 0, embedded = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { PackRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat")) { embedded++; continue; } // materials embedded in model files are read-only
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                Convert(m, shader);
                converted++;
            }
            AssetDatabase.SaveAssets();
            int palettes = FixPaletteTextures();
            return $"Pack materials converted: {converted} (embedded skipped: {embedded}). Palette textures fixed: {palettes}.";
        }

        private static void Convert(Material m, Shader target)
        {
            var saved = ReadSaved(m);
            var baseTex = First(saved.textures, BaseSlots);
            var normal = First(saved.textures, NormalSlots);
            var emission = First(saved.textures, EmissionSlots);

            bool foliage = saved.textures.ContainsKey("_Leaf_Texture") || m.name.Contains("Leaf") || m.name.Contains("Fern");
            bool alphaClip = foliage || AnyFlag(saved, AlphaClipFlags);
            float cutoff = 0.5f;
            foreach (var k in CutoffFloats)
                if (saved.floats.TryGetValue(k, out var v) && v > 0.01f) { cutoff = v; break; }

            m.shader = target;
            m.SetTexture(TargetBaseMap, baseTex);
            m.SetColor(TargetBaseColor, Color.white);
            m.SetFloat(TargetSmoothness, 0.15f);

            // URP Lit keyword/toggle names - change when targeting a custom shader.
            m.SetFloat("_AlphaClip", alphaClip ? 1 : 0);
            SetKeyword(m, "_ALPHATEST_ON", alphaClip);
            m.SetFloat(TargetCutoff, cutoff);
            m.SetFloat(TargetCull, foliage ? 0f : 2f); // foliage cards are double sided

            if (normal != null && !foliage) m.SetTexture(TargetNormalMap, normal);
            SetKeyword(m, "_NORMALMAP", normal != null && !foliage);

            SetKeyword(m, "_EMISSION", emission != null);
            if (emission != null)
            {
                m.SetTexture(TargetEmissionMap, emission);
                m.SetColor(TargetEmissionColor, Color.white);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            EditorUtility.SetDirty(m);
        }

        private static int FixPaletteTextures()
        {
            if (string.IsNullOrEmpty(PaletteTextureFilter)) return 0;
            int fixedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets(PaletteTextureFilter, new[] { PackRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null || tex.width > PaletteMaxSize || tex.height > PaletteMaxSize) continue;
                if (!importer.mipmapEnabled && importer.filterMode == FilterMode.Point
                    && importer.textureCompression == TextureImporterCompression.Uncompressed) continue;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                fixedCount++;
            }
            return fixedCount;
        }

        // ------------------------------------------------------------------ saved properties

        private class Saved
        {
            public readonly Dictionary<string, Texture> textures = new();
            public readonly Dictionary<string, float> floats = new();
        }

        /// <summary>Reads m_SavedProperties directly: these survive shader swaps and missing shaders.</summary>
        private static Saved ReadSaved(Material m)
        {
            var saved = new Saved();
            var so = new SerializedObject(m);
            var te = so.FindProperty("m_SavedProperties.m_TexEnvs");
            for (int i = 0; i < te.arraySize; i++)
            {
                var e = te.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("second.m_Texture").objectReferenceValue is Texture tex)
                    saved.textures[e.FindPropertyRelative("first").stringValue] = tex;
            }
            var fl = so.FindProperty("m_SavedProperties.m_Floats");
            for (int i = 0; i < fl.arraySize; i++)
            {
                var e = fl.GetArrayElementAtIndex(i);
                saved.floats[e.FindPropertyRelative("first").stringValue] = e.FindPropertyRelative("second").floatValue;
            }
            return saved;
        }

        private static Texture First(Dictionary<string, Texture> textures, string[] slots)
        {
            foreach (var s in slots)
                if (textures.TryGetValue(s, out var t) && t != null) return t;
            return null;
        }

        private static bool AnyFlag(Saved s, string[] keys)
        {
            foreach (var k in keys)
                if (s.floats.TryGetValue(k, out var v) && v > 0.5f) return true;
            return false;
        }

        private static void SetKeyword(Material m, string keyword, bool on)
        {
            if (on) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
        }
    }
}

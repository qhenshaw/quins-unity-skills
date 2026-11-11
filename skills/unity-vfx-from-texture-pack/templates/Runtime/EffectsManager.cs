using System;
using UnityEngine;
using UnityEngine.VFX;

namespace VfxToolkit
{
    /// <summary>
    /// Plays pooled VFX Graph effects. Each effect type has one long-lived VisualEffect;
    /// bursts are triggered through the "OnPlay" event with position / color / size attributes,
    /// so hundreds of hits per second cost a single component per type.
    /// </summary>
    public class EffectsManager : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public EffectType type;
            public VisualEffectAsset asset;
            [Tooltip("Multiplier applied to the size attribute sent with each burst.")]
            public float sizeScale = 1f;
        }

        public static EffectsManager Instance { get; private set; }

        [SerializeField] private Entry[] effects = Array.Empty<Entry>();

        private static readonly int PlayEventId = Shader.PropertyToID("OnPlay");
        private static readonly int PositionId = Shader.PropertyToID("position");
        private static readonly int ColorId = Shader.PropertyToID("color");
        private static readonly int SizeId = Shader.PropertyToID("size");
        private static readonly int DirectionId = Shader.PropertyToID("direction");

        private VisualEffect[] _byType;
        private float[] _sizeScale;
        private VFXEventAttribute[] _attributes;

        private void Awake()
        {
            Instance = this;
            int n = Enum.GetValues(typeof(EffectType)).Length;
            _byType = new VisualEffect[n];
            _sizeScale = new float[n];
            _attributes = new VFXEventAttribute[n];
            foreach (var e in effects)
            {
                if (e.asset == null) continue;
                var go = new GameObject("VFX_" + e.type);
                go.transform.SetParent(transform, false);
                var vfx = go.AddComponent<VisualEffect>();
                vfx.visualEffectAsset = e.asset;
                // Bursts are placed in world space by attribute, so keep bounds generous.
                _byType[(int)e.type] = vfx;
                _sizeScale[(int)e.type] = e.sizeScale;
                _attributes[(int)e.type] = vfx.CreateVFXEventAttribute();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>True when a VFX Graph is assigned for <paramref name="type"/>.</summary>
        public bool Has(EffectType type) => _byType != null && _byType[(int)type] != null;

        /// <summary>Live particle count of an effect type (tests / debugging).</summary>
        public int AliveParticles(EffectType type) => Has(type) ? _byType[(int)type].aliveParticleCount : 0;

        public static void Play(EffectType type, Vector3 position, Color color, float size = 1f, Vector3 direction = default)
        {
            var m = Instance;
            if (m == null) return;
            var vfx = m._byType[(int)type];
            if (vfx == null) return;
            var attr = m._attributes[(int)type];
            attr.SetVector3(PositionId, position);
            attr.SetVector3(ColorId, new Vector3(color.r, color.g, color.b));
            attr.SetFloat(SizeId, size * m._sizeScale[(int)type]);
            attr.SetVector3(DirectionId, direction);
            vfx.SendEvent(PlayEventId, attr);
        }
    }
}

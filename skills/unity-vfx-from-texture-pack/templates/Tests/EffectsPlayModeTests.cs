// Template PlayMode test: every EffectType has a graph that actually emits particles.
// Adapt the scene loading to your project (load the gameplay scene, wait for EffectsManager.Instance).
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace VfxToolkit.Tests
{
    public class EffectsPlayModeTests
    {
        [UnityTest]
        public IEnumerator EveryEffectType_HasGraphAndEmits()
        {
            SceneManager.LoadScene("Game"); // TODO: your scene that contains EffectsManager
            float timeout = Time.realtimeSinceStartup + 15f;
            while (EffectsManager.Instance == null && Time.realtimeSinceStartup < timeout) yield return null;
            var fx = EffectsManager.Instance;
            Assert.IsNotNull(fx, "EffectsManager should exist in the scene");

            var pos = Vector3.up;
            foreach (EffectType t in System.Enum.GetValues(typeof(EffectType)))
            {
                Assert.IsTrue(fx.Has(t), $"No VFX graph assigned for {t}");
                EffectsManager.Play(t, pos, Color.white, 1.5f, Vector3.forward);
            }

            // aliveParticleCount is read back from the GPU a few frames late and bursts are short: poll, don't wait a fixed frame count.
            foreach (EffectType t in System.Enum.GetValues(typeof(EffectType)))
            {
                bool seen = false;
                for (int i = 0; i < 30 && !seen; i++) { seen = fx.AliveParticles(t) > 0; if (!seen) yield return null; }
                Assert.IsTrue(seen, $"{t} should emit particles when played");
            }
        }
    }
}

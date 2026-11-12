using System.Collections.Generic;
using MyGame.DebugTools;
using UnityEngine;

namespace MyGame.Enemies
{
    /// <summary>
    /// Example of a feature adding its own section to the debug menu. It lives in the feature's assembly (which
    /// references MyGame.DebugTools), in the feature's Scripts/Debug/ folder, and sits on the debug menu's
    /// GameObject so the section is there in every scene that has the menu.
    ///
    /// Replace the stand-ins (the spawned list, <see cref="FreezeAI"/>) with calls into the real feature. The
    /// shape to keep: buttons for the main actions, toggles for cheats and states, sliders for tuning values and a
    /// readout of live state.
    /// </summary>
    public class EnemiesDebugControls : MonoBehaviour
    {
        // Serialized fields stay outside the #if so the component's data is the same in every build.
        [Tooltip("Spawned by \"Spawn enemy\".")]
        [SerializeField] private GameObject spawnable;

        [Tooltip("Enemies are spawned around this point (the player, usually). Uses this object if empty.")]
        [SerializeField] private Transform spawnAround;

        [Tooltip("How far from the point \"Spawn enemy\" puts enemies, in metres.")]
        [SerializeField, Range(1f, 30f)] private float spawnDistance = 8f;

        /// <summary>A cheat the feature's own code reads (e.g. the enemy AI skips its update while this is set).</summary>
        public static bool FreezeAI { get; set; }

        // Statics survive from one Play session to the next when domain reload is off, so reset them here.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => FreezeAI = false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly List<GameObject> spawned = new();

        private void OnEnable()
        {
            // Labels are the CLI's keys (`debugmenu do --section Enemies --label "Kill all"`): keep them stable
            // and unique within the section.
            DebugMenu.Register(this, "Enemies", section =>
            {
                section
                    .Readout("Alive", () => $"{CountAlive()}")
                    .Toggle("Freeze AI", () => FreezeAI, v => FreezeAI = v)
                    .Slider("Spawn distance", 1f, 30f, () => spawnDistance, v => spawnDistance = v)
                    .Button("Kill all", KillAll);

                // Controls that need an asset are added only when it is assigned.
                if (spawnable != null) section.Button("Spawn enemy", Spawn);
            });
        }

        private void OnDisable() => DebugMenu.Unregister(this);

        private int CountAlive()
        {
            spawned.RemoveAll(e => e == null);
            return spawned.Count;
        }

        private void Spawn()
        {
            Transform centre = spawnAround != null ? spawnAround : transform;
            Vector2 offset = Random.insideUnitCircle.normalized * spawnDistance;
            spawned.Add(Instantiate(spawnable, centre.position + new Vector3(offset.x, 0f, offset.y), Quaternion.identity));
        }

        private void KillAll()
        {
            foreach (var enemy in spawned)
            {
                if (enemy != null) Destroy(enemy);
            }

            spawned.Clear();
        }
#endif
    }
}

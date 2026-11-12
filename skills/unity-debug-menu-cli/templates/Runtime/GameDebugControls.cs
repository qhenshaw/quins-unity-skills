using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyGame.DebugTools
{
    /// <summary>General debug controls (time scale, frame rate, scene reload). Lives on the DebugMenu prefab.</summary>
    public class GameDebugControls : MonoBehaviour
    {
        private float smoothedFrameTime;

        private void OnEnable()
        {
            DebugMenu.Register(this, "Game", section => section
                .Readout("FPS", () => smoothedFrameTime > 0f ? $"{1f / smoothedFrameTime:0} ({smoothedFrameTime * 1000f:0.0} ms)" : "-")
                .Slider("Time scale", 0f, 3f, () => Time.timeScale, v => Time.timeScale = v)
                .Button("Reset time scale", () => Time.timeScale = 1f)
                .Button("Reload scene", () => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex)));
        }

        private void OnDisable()
        {
            DebugMenu.Unregister(this);
        }

        private void Update()
        {
            smoothedFrameTime = Mathf.Lerp(smoothedFrameTime <= 0f ? Time.unscaledDeltaTime : smoothedFrameTime,
                Time.unscaledDeltaTime, 0.1f);
        }
    }
}

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace MyGame.Tests
{
    /// <summary>
    /// Examples of the patterns. Replace Player / GamePause / ShopView names with the project's own types.
    /// Each test acts through a device and asserts on game state.
    /// </summary>
    public class ExampleInputTests : InputTestBase
    {
        private IEnumerator StartGame()
        {
            SceneManager.LoadScene("Game");
            yield return WaitFor(() => Object.FindAnyObjectByType<Player>() != null, 15f, "player spawned");
        }

        private static Player Player => Object.FindAnyObjectByType<Player>();

        [UnityTest]
        public IEnumerator Keyboard_D_MovesPlayerRight()
        {
            yield return StartGame();
            var start = Player.transform.position;
            Press(Keyboard.dKey);
            yield return new WaitForSeconds(0.6f);
            Release(Keyboard.dKey);
            Assert.Greater(Player.transform.position.x - start.x, 1f, "Holding D should move the player along +X");
        }

        [UnityTest]
        public IEnumerator Gamepad_LeftStickMoves_SouthJumps()
        {
            var pad = AddGamepad();
            yield return StartGame();
            var start = Player.transform.position;
            Set(pad.leftStick, new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.5f);
            Set(pad.leftStick, Vector2.zero);
            yield return Tap(pad.buttonSouth);
            Assert.Greater(Player.transform.position.z - start.z, 1f, "Stick up should move the player along +Z");
        }

        [UnityTest]
        public IEnumerator Escape_PausesAndBlocksMovement()
        {
            yield return StartGame();
            yield return Tap(Keyboard.escapeKey);
            Assert.AreEqual(0f, Time.timeScale, "Escape should pause");

            var before = Player.transform.position;
            Press(Keyboard.dKey);
            yield return new WaitForSecondsRealtime(0.3f); // scaled waits never finish while paused
            Release(Keyboard.dKey);
            Assert.AreEqual(before, Player.transform.position, "Player must not move while paused");

            yield return Tap(Keyboard.escapeKey);
            Assert.AreEqual(1f, Time.timeScale, "Escape again should resume");
        }

        /// <summary>Builds start at the menu: enter gameplay through the real menu button so disabled action maps show up.</summary>
        [UnityTest]
        public IEnumerator FromMainMenu_PlayButton_EnablesGameplayInput()
        {
            SceneManager.LoadScene("MainMenu");
            yield return null;
            yield return null;
            var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            yield return Click(ScreenCenter(root.Q<Button>("play")));
            yield return WaitFor(() => Player != null, 15f, "game scene from menu");

            var start = Player.transform.position;
            Press(Keyboard.aKey);
            yield return new WaitForSeconds(0.5f);
            Release(Keyboard.aKey);
            Assert.Less(Player.transform.position.x - start.x, -1f, "Gameplay input should work after the menu");
        }

        [UnityTest]
        public IEnumerator DragItemOntoSlot_PlacesIt()
        {
            yield return StartGame();
            var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            var item = root.Q("inventory-item-0");
            var slot = root.Q("equip-slot");
            yield return Drag(ScreenCenter(item), ScreenCenter(slot));
            Assert.AreEqual(1, slot.childCount, "The dragged item should end up in the slot");
        }

        [UnityTest]
        public IEnumerator Hover_ShowsTooltip()
        {
            yield return StartGame();
            var root = Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
            yield return MoveMouse(ScreenCenter(root.Q("inventory-item-0")), 3); // MoveMouse nudges away first
            Assert.AreEqual(DisplayStyle.Flex, root.Q("tooltip").resolvedStyle.display, "Hover should show the tooltip");
        }
    }
}

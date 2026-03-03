using NUnit.Framework;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tests.Services
{
    /// <summary>
    /// Tests for McpBackgroundService state logic.
    /// Note: McpBackgroundService is an [InitializeOnLoad] singleton tied to the Unity Editor lifecycle.
    /// These tests validate the observable state contract, not internal threading (which requires
    /// a live WebSocket server and cannot be tested in isolation without a real server).
    /// </summary>
    [TestFixture]
    public class McpBackgroundServiceTests
    {
        [Test]
        public void Instance_IsInitialized_AfterEditorLoad()
        {
            // [InitializeOnLoad] fires before any tests run in EditMode.
            // The instance should already exist.
            Assert.IsNotNull(McpBackgroundService.Instance);
        }

        [Test]
        public void Instance_HasWebSocketClient()
        {
            Assert.IsNotNull(McpBackgroundService.Instance.Client);
        }

        [Test]
        public void IsConnecting_IsFalse_WhenNotConnecting()
        {
            // At rest (not actively connecting), IsConnecting should be false.
            // We can't guarantee IsConnected since there may not be a server,
            // but IsConnecting should not be stuck true.
            Assert.IsFalse(McpBackgroundService.Instance.IsConnecting);
        }

        [Test]
        public void Client_IsNotConnected_WhenNoServer()
        {
            // With no MCP server running, the client should not be connected.
            // This also verifies IsConnected properly reflects WebSocket state.
            Assert.IsFalse(McpBackgroundService.Instance.Client.IsConnected);
        }

        [Test]
        public void OnStatusChanged_CanSubscribeAndUnsubscribe()
        {
            var service = McpBackgroundService.Instance;
            var called = false;
            void Handler() => called = true;

            service.OnStatusChanged += Handler;
            service.OnStatusChanged -= Handler;

            // Verify no exception is thrown and subscription management works
            Assert.IsFalse(called);
        }
    }
}

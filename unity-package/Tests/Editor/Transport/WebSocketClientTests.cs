using System.Reflection;
using NUnit.Framework;
using UnityAITools.Editor.Transport;

namespace UnityAITools.Editor.Tests.Transport
{
    [TestFixture]
    public class WebSocketClientTests
    {
        [Test]
        public void TryExtractCommandId_ParsesJsonId()
        {
            var method = typeof(WebSocketClient).GetMethod(
                "TryExtractCommandId",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method);

            var json = "{\"type\":\"execute_command\",\"id\":\"cmd-123\",\"name\":\"read_console\",\"params\":{}}";
            var id = method.Invoke(null, new object[] { json }) as string;

            Assert.AreEqual("cmd-123", id);
        }

        [Test]
        public void TryExtractCommandId_MissingId_ReturnsNull()
        {
            var method = typeof(WebSocketClient).GetMethod(
                "TryExtractCommandId",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method);

            var json = "{\"type\":\"execute_command\",\"name\":\"read_console\",\"params\":{}}";
            var id = method.Invoke(null, new object[] { json }) as string;

            Assert.IsNull(id);
        }
    }
}

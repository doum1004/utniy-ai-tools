using NUnit.Framework;
using UnityAITools.Editor.Transport;

namespace UnityAITools.Editor.Tests.Transport
{
    [TestFixture]
    public class MessageTypesTests
    {
        [Test]
        public void RegisterMessage_HasCorrectType()
        {
            var msg = new RegisterMessage();
            Assert.AreEqual("register", msg.type);
        }

        [Test]
        public void RegisterMessage_HoldsProjectData()
        {
            var msg = new RegisterMessage
            {
                project_name = "TestProject",
                project_hash = "abc123",
                unity_version = "2022.3.0f1",
                project_path = "/path/to/project"
            };

            Assert.AreEqual("TestProject", msg.project_name);
            Assert.AreEqual("abc123", msg.project_hash);
            Assert.AreEqual("2022.3.0f1", msg.unity_version);
            Assert.AreEqual("/path/to/project", msg.project_path);
        }

        [Test]
        public void CommandResultMessage_DefaultConstructor_SetsType()
        {
            var msg = new CommandResultMessage();
            Assert.AreEqual("command_result", msg.type);
        }

        [Test]
        public void CommandResultMessage_ParameterizedConstructor_SetsFields()
        {
            var result = new CommandResult
            {
                success = true,
                data = "test data"
            };
            var msg = new CommandResultMessage("cmd-1", result);

            Assert.AreEqual("command_result", msg.type);
            Assert.AreEqual("cmd-1", msg.id);
            Assert.IsTrue(msg.result.success);
            Assert.AreEqual("test data", msg.result.data);
        }

        [Test]
        public void PongMessage_DefaultConstructor_SetsType()
        {
            var msg = new PongMessage();
            Assert.AreEqual("pong", msg.type);
        }

        [Test]
        public void PongMessage_ParameterizedConstructor_SetsSessionId()
        {
            var msg = new PongMessage("session-123");
            Assert.AreEqual("pong", msg.type);
            Assert.AreEqual("session-123", msg.session_id);
        }

        [Test]
        public void CommandResult_SuccessFields()
        {
            var result = new CommandResult
            {
                success = true,
                data = new { count = 5 },
                hint = "some hint"
            };

            Assert.IsTrue(result.success);
            Assert.IsNotNull(result.data);
            Assert.AreEqual("some hint", result.hint);
            Assert.IsNull(result.error);
        }

        [Test]
        public void CommandResult_ErrorFields()
        {
            var result = new CommandResult
            {
                success = false,
                error = "Something failed"
            };

            Assert.IsFalse(result.success);
            Assert.AreEqual("Something failed", result.error);
            Assert.IsNull(result.data);
        }

        [Test]
        public void WelcomeMessage_Fields()
        {
            var msg = new WelcomeMessage
            {
                type = "welcome",
                serverTimeout = 30,
                keepAliveInterval = 15
            };

            Assert.AreEqual("welcome", msg.type);
            Assert.AreEqual(30, msg.serverTimeout);
            Assert.AreEqual(15, msg.keepAliveInterval);
        }

        [Test]
        public void ExecuteCommandMessage_Fields()
        {
            var msg = new ExecuteCommandMessage
            {
                type = "execute_command",
                id = "cmd-1",
                name = "manage_scene",
                timeout = 30f
            };

            Assert.AreEqual("execute_command", msg.type);
            Assert.AreEqual("cmd-1", msg.id);
            Assert.AreEqual("manage_scene", msg.name);
            Assert.AreEqual(30f, msg.timeout);
        }

        [Test]
        public void RegisteredMessage_Fields()
        {
            var msg = new RegisteredMessage
            {
                type = "registered",
                session_id = "session-abc"
            };

            Assert.AreEqual("registered", msg.type);
            Assert.AreEqual("session-abc", msg.session_id);
        }
    }
}

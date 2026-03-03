using System.Threading.Tasks;
using NUnit.Framework;
using UnityAITools.Editor.Transport;

namespace UnityAITools.Editor.Tests.Transport
{
    [TestFixture]
    public class CommandDispatcherTests
    {
        private CommandDispatcher _dispatcher;

        [SetUp]
        public void SetUp()
        {
            _dispatcher = new CommandDispatcher();
        }

        [Test]
        public async Task DispatchAsync_WithRegisteredHandler_CallsHandler()
        {
            var handler = new MockToolHandler(new CommandResult { success = true, data = "ok" });
            _dispatcher.RegisterHandler("test_command", handler);

            var json = "{\"name\":\"test_command\",\"params\":{\"key\":\"value\"}}";
            var result = await _dispatcher.DispatchAsync(json);

            Assert.IsTrue(result.success);
            Assert.AreEqual("test_command", handler.LastCommandName);
            Assert.IsNotNull(handler.LastParamsJson);
        }

        [Test]
        public async Task DispatchAsync_WithUnknownCommand_ReturnsError()
        {
            var json = "{\"name\":\"unknown_command\",\"params\":{}}";
            var result = await _dispatcher.DispatchAsync(json);

            Assert.IsFalse(result.success);
            Assert.IsTrue(result.error.Contains("Unknown command"));
            Assert.IsTrue(result.error.Contains("unknown_command"));
        }

        [Test]
        public async Task DispatchAsync_WithMissingName_ReturnsError()
        {
            var json = "{\"params\":{}}";
            var result = await _dispatcher.DispatchAsync(json);

            Assert.IsFalse(result.success);
            Assert.IsTrue(result.error.Contains("missing 'name' field"));
        }

        [Test]
        public async Task DispatchAsync_WithInvalidJson_ReturnsError()
        {
            var result = await _dispatcher.DispatchAsync("not json at all{{{");

            Assert.IsFalse(result.success);
            Assert.IsNotNull(result.error);
        }

        [Test]
        public async Task DispatchAsync_FiresOnCommandExecuted()
        {
            var handler = new MockToolHandler(new CommandResult { success = true });
            _dispatcher.RegisterHandler("test_command", handler);

            string executedCommand = null;
            _dispatcher.OnCommandExecuted += (cmd) => executedCommand = cmd;

            var json = "{\"name\":\"test_command\",\"params\":{}}";
            await _dispatcher.DispatchAsync(json);

            Assert.AreEqual("test_command", executedCommand);
        }

        [Test]
        public async Task DispatchAsync_DoesNotFireOnCommandExecuted_OnError()
        {
            string executedCommand = null;
            _dispatcher.OnCommandExecuted += (cmd) => executedCommand = cmd;

            var json = "{\"name\":\"unknown\",\"params\":{}}";
            await _dispatcher.DispatchAsync(json);

            Assert.IsNull(executedCommand);
        }

        [Test]
        public async Task DispatchAsync_ExtractsNestedParams()
        {
            var handler = new MockToolHandler(new CommandResult { success = true });
            _dispatcher.RegisterHandler("test", handler);

            var json = "{\"name\":\"test\",\"params\":{\"nested\":{\"a\":1,\"b\":2}}}";
            await _dispatcher.DispatchAsync(json);

            Assert.IsTrue(handler.LastParamsJson.Contains("nested"));
            Assert.IsTrue(handler.LastParamsJson.Contains("\"a\":1"));
        }

        [Test]
        public void RegisterHandler_OverwritesPreviousHandler()
        {
            var handler1 = new MockToolHandler(new CommandResult { success = true, data = "first" });
            var handler2 = new MockToolHandler(new CommandResult { success = true, data = "second" });

            _dispatcher.RegisterHandler("test", handler1);
            _dispatcher.RegisterHandler("test", handler2);

            var json = "{\"name\":\"test\",\"params\":{}}";
            var task = _dispatcher.DispatchAsync(json);
            task.Wait();

            Assert.IsNull(handler1.LastCommandName);
            Assert.AreEqual("test", handler2.LastCommandName);
        }

        /// <summary>
        /// Simple mock IToolHandler for testing.
        /// </summary>
        private class MockToolHandler : IToolHandler
        {
            private readonly CommandResult _result;
            public string LastCommandName { get; private set; }
            public string LastParamsJson { get; private set; }

            public MockToolHandler(CommandResult result)
            {
                _result = result;
            }

            public Task<CommandResult> ExecuteAsync(string commandName, string paramsJson)
            {
                LastCommandName = commandName;
                LastParamsJson = paramsJson;
                return Task.FromResult(_result);
            }
        }
    }
}

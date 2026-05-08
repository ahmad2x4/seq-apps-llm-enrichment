using System.Collections.Generic;
using Seq.Apps;
using Serilog;

namespace Seq.Apps.LlmEnrichment.Tests.Support
{
    class TestAppHost : IAppHost
    {
        public App App { get; } = new("app-1", "Test App", new Dictionary<string, string>(), "//TEST");
        public Host Host { get; } = new Host("https://seq.example.com", "test");
        public ILogger Logger { get; } = new LoggerConfiguration().CreateLogger();
        public string StoragePath { get; } = "//TEST";
    }
}

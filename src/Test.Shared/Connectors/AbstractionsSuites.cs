namespace Test.Shared.Connectors
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the connector contract helpers.
    /// </summary>
    public static class AbstractionsSuites
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(new TestCaseDescriptor("connectors.abstractions", "tool-arguments-typed", "ToolArguments reads typed values", ct =>
            {
                ToolArguments args = new ToolArguments("{\"a\":\"x\",\"n\":5,\"b\":true,\"l\":[\"p\",\"q\"],\"m\":{\"k\":\"v\"}}");
                Assert(args.GetString("a") == "x", "string");
                Assert(args.GetInt("n", 0, 0, 3) == 3, "clamped int");
                Assert(args.GetBool("b", false), "bool");
                Assert(args.GetStringList("l").Count == 2, "list");
                Assert(args.GetStringMap("m")["k"] == "v", "map");
                Assert(!args.Has("missing"), "missing");
                return Task.CompletedTask;
            }));
            cases.Add(new TestCaseDescriptor("connectors.abstractions", "output-limiter-head-tail", "OutputLimiter truncates by lines and bytes", ct =>
            {
                string text = "1\n2\n3\n4\n5\n";
                ToolResult r = new ToolResult();
                string head = OutputLimiter.Head(text, 1024, 2, r);
                Assert(head.StartsWith("1\n2\n", StringComparison.Ordinal) && r.Truncated, "head lines");
                string tail = OutputLimiter.Tail(text, 1024, 2, null);
                Assert(tail.EndsWith("4\n5\n", StringComparison.Ordinal), "tail lines");
                string big = OutputLimiter.Head(new string('x', 5000), 1024, 0, null);
                Assert(big.Length < 1200, "head bytes");
                return Task.CompletedTask;
            }));
            cases.Add(new TestCaseDescriptor("connectors.abstractions", "secret-accessor-dispose", "SecretAccessor clears on dispose", ct =>
            {
                SecretAccessor s = new SecretAccessor(new Dictionary<string, Dictionary<string, string>> { { "password", new Dictionary<string, string> { { "password", "hunter2" } } } });
                Assert(s.Get("password", "password") == "hunter2", "get");
                s.Dispose();
                bool threw = false;
                try { s.Get("password", "password"); } catch (ObjectDisposedException) { threw = true; }
                Assert(threw, "disposed");
                return Task.CompletedTask;
            }));
            return new List<TestSuiteDescriptor> { new TestSuiteDescriptor("connectors.abstractions", "Connector contract helpers", cases) };
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }
    }
}

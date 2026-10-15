namespace Test.Shared.Connectors
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;
    using Hugo.Connectors.Redis.Classification;
    using Test.Shared.Connectors.Redis;
    using Touchstone.Core;

    /// <summary>
    /// Suites for the Redis connector. Unit and classifier suites always run; the testbed suite runs when HUGO_TESTBED=1.
    /// </summary>
    public static class RedisSuites
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return new List<TestSuiteDescriptor>
            {
                new TestSuiteDescriptor(RedisUnitCases.SuiteId, "Redis connector unit tests", RedisUnitCases.Build()),
                ClassifierSuite(),
                RedisTestbedCases.Build()
            };
        }

        private static TestSuiteDescriptor ClassifierSuite()
        {
            const string suiteId = "connectors.redis.classifier";
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            List<RedisClassifierCase> table = RedisClassifierTable.Cases();
            for (int i = 0; i < table.Count; i++)
            {
                RedisClassifierCase row = table[i];
                string id = "case-" + (i + 1).ToString("D3", CultureInfo.InvariantCulture);
                cases.Add(new TestCaseDescriptor(suiteId, id, row.Expected + ": " + row.Command, ct =>
                {
                    RedisClassifierOptions options = new RedisClassifierOptions
                    {
                        Denied = new HashSet<string>(row.Denied, StringComparer.OrdinalIgnoreCase),
                        ExtraReadOnly = new HashSet<string>(row.ReadOnly, StringComparer.OrdinalIgnoreCase)
                    };
                    EffectClassification c;
                    try
                    {
                        c = RedisCommandClassifier.Classify(RedisCommandTokenizer.Tokenize(row.Command), options);
                    }
                    catch (ConnectorException e)
                    {
                        c = EffectClassification.Reject(e.Message);
                    }
                    string actual = c.Rejected ? "X" : c.Effect == EffectEnum.Read ? "R" : c.Effect == EffectEnum.Write ? "W" : (c.AlwaysRequireApproval ? "A" : "D");
                    if (actual != row.Expected)
                        throw new InvalidOperationException("Expected " + row.Expected + " but classified " + actual + " (" + c.Reason + ") for: " + row.Command);
                    if (String.IsNullOrEmpty(c.Reason)) throw new InvalidOperationException("Classification has no reason for: " + row.Command);
                    return Task.CompletedTask;
                }));
            }
            cases.Add(new TestCaseDescriptor(suiteId, "table-size", "The table has at least 200 cases and covers every outcome", ct =>
            {
                if (table.Count < 200) throw new InvalidOperationException("Only " + table.Count + " classifier cases.");
                foreach (string outcome in new[] { "R", "W", "D", "A", "X" })
                    if (!table.Any(r => r.Expected == outcome)) throw new InvalidOperationException("No case expects " + outcome + ".");
                return Task.CompletedTask;
            }));
            return new TestSuiteDescriptor(suiteId, "Redis command classifier table", cases);
        }
    }
}

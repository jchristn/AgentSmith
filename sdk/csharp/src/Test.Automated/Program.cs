namespace Test.Automated
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Test.Automated.Suites;

    /// <summary>
    /// Hugo C# SDK automated tests. Runs against a live server (HUGO_SDK_SERVER, default http://127.0.0.1:8800),
    /// provisions its own tenant with the administrator account, and deletes it at the end. Exit code 0 when no test
    /// failed, 1 when a test failed, 2 when setup failed.
    /// </summary>
    public static class Program
    {
        #region Public-Methods

        /// <summary>
        /// Entry point.
        /// </summary>
        /// <param name="args">Optional suite names to run (case-insensitive); all suites when empty.</param>
        /// <returns>Exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            string line = new string('-', 78);
            TestEnvironment env = TestEnvironment.FromEnvironment();
            Console.WriteLine();
            Console.WriteLine("Hugo C# SDK Automated Tests");
            Console.WriteLine(line);
            Console.WriteLine("Server : " + env.ServerUrl);
            Console.WriteLine("Admin  : " + env.AdminEmail + " (tenant " + env.AdminTenantId + ")");
            Console.WriteLine(line);

            List<TestSuite> suites = new List<TestSuite>
            {
                new UnitSuite(),
                new AuthSuite(),
                new IdentitySuite(),
                new RbacSuite(),
                new SecretsSuite(),
                new AdminSuite(),
                new DomainSuite(),
                new ModelSuite()
            };
            if (args.Length > 0) suites = suites.Where(s => args.Any(a => s.Name.StartsWith(a, StringComparison.OrdinalIgnoreCase))).ToList();

            TestRunner runner = new TestRunner();
            using (HarnessContext ctx = new HarnessContext(env))
            {
                try
                {
                    await ctx.SetupAsync().ConfigureAwait(false);
                    Console.WriteLine("Provisioned tenant " + ctx.TenantId + " with owner " + ctx.OwnerEmail);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Setup failed: " + e.Message);
                    await ctx.CleanupAsync().ConfigureAwait(false);
                    return 2;
                }

                try
                {
                    foreach (TestSuite suite in suites)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Suite: " + suite.Name);
                        runner.Suite = suite.Name;
                        await suite.RunAsync(runner, ctx).ConfigureAwait(false);
                    }
                }
                finally
                {
                    Console.WriteLine();
                    await ctx.CleanupAsync().ConfigureAwait(false);
                }
            }

            int passed = runner.Results.Count(r => r.Outcome == TestOutcomeEnum.Passed);
            int failed = runner.Results.Count(r => r.Outcome == TestOutcomeEnum.Failed);
            int skipped = runner.Results.Count(r => r.Outcome == TestOutcomeEnum.Skipped);

            Console.WriteLine();
            Console.WriteLine("Summary");
            Console.WriteLine(line);
            foreach (TestResult result in runner.Results.Where(r => r.Outcome != TestOutcomeEnum.Passed))
            {
                Console.WriteLine("| " + result.ToString());
                if (result.Exception != null && result.Outcome == TestOutcomeEnum.Failed) Console.WriteLine(result.Exception.ToString());
            }
            Console.WriteLine(passed + " passed, " + failed + " failed, " + skipped + " skipped (" + runner.Results.Count + " total)");
            Console.WriteLine(failed == 0 ? "Test succeeded" : "Test failed");
            Console.WriteLine();
            return failed == 0 ? 0 : 1;
        }

        #endregion
    }
}

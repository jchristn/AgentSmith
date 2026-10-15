namespace Test.Automated
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Sdk;

    /// <summary>
    /// Runs named tests and records their results.
    /// </summary>
    public class TestRunner
    {
        #region Public-Members

        /// <summary>
        /// Results in run order.
        /// </summary>
        public List<TestResult> Results { get; } = new List<TestResult>();

        /// <summary>
        /// Current suite name.
        /// </summary>
        public string Suite { get; set; } = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one test. TestSkippedException marks it skipped; any other exception fails it.
        /// </summary>
        /// <param name="name">Test name.</param>
        /// <param name="test">Test body.</param>
        /// <returns>Result.</returns>
        public async Task<TestResult> RunAsync(string name, Func<Task> test)
        {
            TestResult result = new TestResult();
            result.Suite = Suite;
            result.Name = name;
            result.StartUtc = DateTime.UtcNow;
            try
            {
                await test().ConfigureAwait(false);
                result.Outcome = TestOutcomeEnum.Passed;
            }
            catch (TestSkippedException e)
            {
                result.Outcome = TestOutcomeEnum.Skipped;
                result.Message = e.Message;
            }
            catch (HugoException e)
            {
                result.Outcome = TestOutcomeEnum.Failed;
                result.Message = e.Message + (e.RequestId != null ? " (request " + e.RequestId + ")" : String.Empty);
                result.Exception = e;
            }
            catch (Exception e)
            {
                result.Outcome = TestOutcomeEnum.Failed;
                result.Message = e.GetType().Name + ": " + e.Message;
                result.Exception = e;
            }
            result.EndUtc = DateTime.UtcNow;
            Results.Add(result);
            Console.WriteLine("| " + result.ToString());
            return result;
        }

        #endregion
    }
}

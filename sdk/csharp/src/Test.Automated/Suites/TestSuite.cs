namespace Test.Automated.Suites
{
    using System;
    using System.Threading.Tasks;
    using Hugo.Sdk;

    /// <summary>
    /// A group of related tests.
    /// </summary>
    public abstract class TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite name.
        /// </summary>
        public abstract string Name { get; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the suite's tests.
        /// </summary>
        /// <param name="runner">Runner.</param>
        /// <param name="ctx">Harness context.</param>
        /// <returns>Task.</returns>
        public abstract Task RunAsync(TestRunner runner, HarnessContext ctx);

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Unique name for a test entity.
        /// </summary>
        /// <param name="prefix">Prefix.</param>
        /// <returns>Name.</returns>
        protected static string Unique(string prefix)
        {
            return prefix + "-" + HarnessContext.RandomHex(4);
        }

        /// <summary>
        /// Run a probe call and convert a 404 (route not implemented yet) into a skip.
        /// </summary>
        /// <param name="probe">Probe call, usually the list route.</param>
        /// <returns>Task.</returns>
        /// <exception cref="TestSkippedException">Thrown when the server returns 404.</exception>
        protected static async Task RequireRouteAsync(Func<Task> probe)
        {
            try
            {
                await probe().ConfigureAwait(false);
            }
            catch (HugoException e) when (e.StatusCode == 404)
            {
                throw new TestSkippedException("route not available on this server yet (Phase 3/5/6 preview): " + (e.Code ?? "NotFound"));
            }
        }

        #endregion
    }
}

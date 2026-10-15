namespace Test.Automated
{
    using System;

    /// <summary>
    /// Result of one test.
    /// </summary>
    public class TestResult
    {
        #region Public-Members

        /// <summary>
        /// Suite name.
        /// </summary>
        public string Suite { get; set; } = String.Empty;

        /// <summary>
        /// Test name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Outcome.
        /// </summary>
        public TestOutcomeEnum Outcome { get; set; } = TestOutcomeEnum.Passed;

        /// <summary>
        /// Failure or skip message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Exception for failures.
        /// </summary>
        public Exception? Exception { get; set; } = null;

        /// <summary>
        /// Start timestamp, UTC.
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// End timestamp, UTC.
        /// </summary>
        public DateTime EndUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Runtime.
        /// </summary>
        public TimeSpan Runtime
        {
            get
            {
                return EndUtc - StartUtc;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// One-line summary.
        /// </summary>
        /// <returns>Text.</returns>
        public override string ToString()
        {
            string label = Outcome == TestOutcomeEnum.Passed ? "PASS" : (Outcome == TestOutcomeEnum.Failed ? "FAIL" : "SKIP");
            return label + "  " + Suite + " / " + Name + " (" + (int)Runtime.TotalMilliseconds + "ms)" + (String.IsNullOrEmpty(Message) ? String.Empty : ": " + Message);
        }

        #endregion
    }
}

namespace Hugo.Connectors.Observability
{
    using System;

    /// <summary>
    /// The resolution step of a range query, chosen so that every series stays within a point budget. Immutable and
    /// thread-safe.
    /// </summary>
    public class PromStepPlan
    {
        #region Public-Members

        /// <summary>
        /// Step in whole seconds. At least 1.
        /// </summary>
        public long StepSeconds { get; }

        /// <summary>
        /// Whether a requested step was raised to respect the point budget.
        /// </summary>
        public bool Raised { get; }

        /// <summary>
        /// Points per series at this step (at most).
        /// </summary>
        public long Points { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="stepSeconds">Step in seconds.</param>
        /// <param name="raised">Whether a requested step was raised.</param>
        /// <param name="points">Points per series.</param>
        public PromStepPlan(long stepSeconds, bool raised, long points)
        {
            StepSeconds = Math.Max(1, stepSeconds);
            Raised = raised;
            Points = points;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Choose a step for a window.
        /// </summary>
        /// <param name="window">Window length.</param>
        /// <param name="requested">Requested step, or null to pick one.</param>
        /// <param name="maxPoints">Point budget per series (at least 2).</param>
        /// <param name="defaultPoints">Points aimed for when no step is requested (capped by maxPoints).</param>
        /// <returns>Plan.</returns>
        public static PromStepPlan Compute(TimeSpan window, TimeSpan? requested, int maxPoints, int defaultPoints)
        {
            double seconds = Math.Max(1, window.TotalSeconds);
            int budget = Math.Max(2, maxPoints);
            long minimum = (long)Math.Ceiling(seconds / (budget - 1));
            if (minimum < 1) minimum = 1;

            if (requested.HasValue && requested.Value.TotalSeconds > 0)
            {
                long asked = (long)Math.Ceiling(requested.Value.TotalSeconds);
                if (asked < 1) asked = 1;
                if (asked >= minimum) return new PromStepPlan(asked, false, PointsFor(seconds, asked));
                return new PromStepPlan(minimum, true, PointsFor(seconds, minimum));
            }

            int target = Math.Max(2, Math.Min(budget, defaultPoints));
            long chosen = (long)Math.Ceiling(seconds / (target - 1));
            if (chosen < minimum) chosen = minimum;
            if (chosen < 1) chosen = 1;
            return new PromStepPlan(chosen, false, PointsFor(seconds, chosen));
        }

        #endregion

        #region Private-Methods

        private static long PointsFor(double seconds, long step)
        {
            return (long)Math.Floor(seconds / step) + 1;
        }

        #endregion
    }
}

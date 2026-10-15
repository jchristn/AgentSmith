namespace Hugo.Connectors.Aws
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Classifies AWS API operations by name, per plan section 8.3. <c>Describe</c>, <c>List</c>, and <c>Get</c> are Read.
    /// <c>Start</c>, <c>Update</c>, <c>Put</c>, <c>Create</c>, <c>Modify</c>, and <c>Invoke</c> are Write. <c>Stop</c>,
    /// <c>Terminate</c>, <c>Delete</c>, and <c>Reboot</c> are Destructive. A short table of read-only exceptions covers
    /// operations whose verb misleads (Logs Insights <c>StartQuery</c>, <c>FilterLogEvents</c>). Anything else is
    /// Destructive (fail closed). Pure and thread-safe.
    /// </summary>
    public static class AwsOperationClassifier
    {
        #region Private-Members

        private static readonly string[] _ReadPrefixes = new string[] { "Describe", "List", "Get" };
        private static readonly string[] _WritePrefixes = new string[] { "Start", "Update", "Put", "Create", "Modify", "Invoke" };
        private static readonly string[] _DestructivePrefixes = new string[] { "Stop", "Terminate", "Delete", "Reboot" };

        private static readonly HashSet<string> _ReadExceptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "logs:StartQuery",
            "logs:StopQuery",
            "logs:FilterLogEvents",
            "sts:GetCallerIdentity"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Classify an operation, for example <c>ec2:TerminateInstances</c> or <c>DescribeInstances</c>.
        /// </summary>
        /// <param name="operation">Operation, optionally prefixed with the IAM service and a colon.</param>
        /// <returns>Effect.</returns>
        public static EffectEnum Classify(string? operation)
        {
            if (String.IsNullOrWhiteSpace(operation)) return EffectEnum.Destructive;
            string op = operation.Trim();
            if (_ReadExceptions.Contains(op)) return EffectEnum.Read;
            int colon = op.IndexOf(':');
            string name = colon >= 0 ? op.Substring(colon + 1) : op;
            if (name.Length == 0) return EffectEnum.Destructive;

            foreach (string p in _DestructivePrefixes) if (StartsWithWord(name, p)) return EffectEnum.Destructive;
            foreach (string p in _ReadPrefixes) if (StartsWithWord(name, p)) return EffectEnum.Read;
            foreach (string p in _WritePrefixes) if (StartsWithWord(name, p)) return EffectEnum.Write;
            return EffectEnum.Destructive;
        }

        /// <summary>
        /// Classify a set of operations by the most severe one.
        /// </summary>
        /// <param name="operations">Operations.</param>
        /// <returns>Effect; Destructive for an empty set.</returns>
        public static EffectEnum ClassifyAll(IEnumerable<string>? operations)
        {
            if (operations == null) return EffectEnum.Destructive;
            bool any = false;
            EffectEnum worst = EffectEnum.Read;
            foreach (string op in operations)
            {
                any = true;
                EffectEnum e = Classify(op);
                if (e > worst) worst = e;
            }
            return any ? worst : EffectEnum.Destructive;
        }

        #endregion

        #region Private-Methods

        private static bool StartsWithWord(string name, string prefix)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) return false;
            if (name.Length == prefix.Length) return true;
            return Char.IsUpper(name[prefix.Length]);
        }

        #endregion
    }
}

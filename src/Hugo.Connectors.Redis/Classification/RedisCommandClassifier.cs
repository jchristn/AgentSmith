namespace Hugo.Connectors.Redis.Classification
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Classifies one Redis command (as an argument list) using <see cref="RedisCommandTable"/>, option-sensitive rules
    /// (<c>SORT ... STORE</c>, <c>COPY ... REPLACE</c>, <c>XREAD ... BLOCK</c>), and the resource's denied and extra
    /// read-only commands. Unknown commands are Destructive. Pure and thread-safe.
    /// </summary>
    public static class RedisCommandClassifier
    {
        #region Public-Methods

        /// <summary>
        /// Classify a command.
        /// </summary>
        /// <param name="arguments">Command name followed by its arguments.</param>
        /// <param name="options">Resource adjustments, or null for defaults.</param>
        /// <returns>Classification with a reason. The approval summary is left empty for the caller to fill.</returns>
        public static EffectClassification Classify(IList<string> arguments, RedisClassifierOptions? options = null)
        {
            RedisClassifierOptions o = options ?? new RedisClassifierOptions();
            if (arguments == null || arguments.Count == 0 || String.IsNullOrWhiteSpace(arguments[0]))
                return EffectClassification.Reject("no Redis command was given");

            string name = arguments[0].Trim().ToUpperInvariant();
            string? sub = arguments.Count > 1 ? arguments[1].Trim().ToUpperInvariant() : null;
            string label = Label(arguments);

            if (o.Denied.Contains(name) || (sub != null && o.Denied.Contains(name + " " + sub)))
            {
                string hint = name == "KEYS" ? "; use redis_scan with a pattern instead" : String.Empty;
                return EffectClassification.Reject("command " + label + " is denied on this resource" + hint);
            }

            RedisCommandSpec? spec = RedisCommandTable.Find(name);
            if (spec == null)
            {
                if (o.ExtraReadOnly.Contains(name) || (sub != null && o.ExtraReadOnly.Contains(name + " " + sub)))
                    return new EffectClassification(EffectEnum.Read, "command " + label + " is listed in readOnlyCommands", String.Empty);
                return new EffectClassification(EffectEnum.Destructive, "unknown command " + name + " (not in the Redis command table; fail closed)", String.Empty);
            }

            if (spec.IsContainer)
            {
                if (sub == null) return new EffectClassification(EffectEnum.Read, "command " + name + " without a subcommand only returns help or an error", String.Empty);
                if (!spec.Subcommands.TryGetValue(sub, out RedisCommandSpec? subSpec))
                    return new EffectClassification(EffectEnum.Destructive, "unknown subcommand " + name + " " + sub + " (fail closed)", String.Empty);
                if (name == "ACL" && sub == "LOG" && HasOption(arguments, 2, "RESET"))
                    return new EffectClassification(EffectEnum.Destructive, "ACL LOG RESET clears the ACL security log", String.Empty);
                return FromSpec(subSpec, name + " " + sub);
            }

            switch (name)
            {
                case "SORT":
                    if (HasOption(arguments, 2, "STORE")) return new EffectClassification(EffectEnum.Write, "SORT with STORE writes the destination key", String.Empty);
                    break;
                case "GEORADIUS":
                case "GEORADIUSBYMEMBER":
                    if (HasOption(arguments, 2, "STORE") || HasOption(arguments, 2, "STOREDIST"))
                        return new EffectClassification(EffectEnum.Write, name + " with STORE writes the destination key", String.Empty);
                    break;
                case "COPY":
                case "RESTORE":
                case "RESTORE-ASKING":
                    if (HasOption(arguments, 2, "REPLACE"))
                        return new EffectClassification(EffectEnum.Destructive, name + " with REPLACE overwrites an existing key", String.Empty);
                    break;
                case "XREAD":
                case "XREADGROUP":
                    if (HasOption(arguments, 1, "BLOCK"))
                        return EffectClassification.Reject(name + " with BLOCK is not supported: it blocks the shared connection; omit BLOCK to read what is available now");
                    break;
            }

            return FromSpec(spec, name);
        }

        /// <summary>
        /// Short label of a command for reasons and summaries: the command name, plus the subcommand of container commands.
        /// </summary>
        /// <param name="arguments">Command name followed by its arguments.</param>
        /// <returns>Label, upper case.</returns>
        public static string Label(IList<string> arguments)
        {
            if (arguments == null || arguments.Count == 0) return String.Empty;
            string name = arguments[0].Trim().ToUpperInvariant();
            RedisCommandSpec? spec = RedisCommandTable.Find(name);
            if (spec != null && spec.IsContainer && arguments.Count > 1) return name + " " + arguments[1].Trim().ToUpperInvariant();
            return name;
        }

        #endregion

        #region Private-Methods

        private static EffectClassification FromSpec(RedisCommandSpec spec, string label)
        {
            if (spec.UnsupportedReason != null) return EffectClassification.Reject(label + " is not supported: " + spec.UnsupportedReason);
            EffectClassification c = new EffectClassification(spec.Effect, "command " + label + " is " + spec.Effect + " in the Redis command table", String.Empty);
            c.AlwaysRequireApproval = spec.AlwaysRequireApproval;
            return c;
        }

        private static bool HasOption(IList<string> arguments, int start, string option)
        {
            for (int i = start; i < arguments.Count; i++)
            {
                if (String.Equals(arguments[i], option, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        #endregion
    }
}

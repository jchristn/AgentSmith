namespace Hugo.Core.Connectors
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Validates resource settings and secret slot bindings against a connector's descriptor (plan P3.2). Pure and
    /// thread-safe.
    /// </summary>
    public static class ConnectorSettingsValidator
    {
        /// <summary>
        /// Maximum time a setting pattern may take to evaluate. Default 250 milliseconds.
        /// </summary>
        public static TimeSpan PatternTimeout { get; set; } = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Validate settings and slot bindings.
        /// </summary>
        /// <param name="descriptor">Connector descriptor.</param>
        /// <param name="values">Setting values keyed by setting key. Null is treated as empty.</param>
        /// <param name="slotKinds">Secret kind bound to each slot, keyed by slot name. Null is treated as empty.</param>
        /// <returns>Validation result with one error per problem.</returns>
        /// <exception cref="ArgumentNullException">Thrown when descriptor is null.</exception>
        public static ConnectorValidationResult Validate(ConnectorDescriptor descriptor, IDictionary<string, string>? values, IDictionary<string, SecretKindEnum>? slotKinds)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            ConnectorValidationResult result = new ConnectorValidationResult();
            Dictionary<string, string> supplied = values == null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(values, StringComparer.Ordinal);

            foreach (string key in supplied.Keys)
            {
                if (descriptor.SettingsSchema.Find(key) == null) result.Add(key, "unknown_setting", "Setting '" + key + "' is not defined by connector '" + descriptor.Type + "'.");
            }

            foreach (SettingDescriptor setting in descriptor.SettingsSchema.Settings)
            {
                supplied.TryGetValue(setting.Key, out string? raw);
                bool empty = String.IsNullOrWhiteSpace(raw);
                if (empty)
                {
                    if (setting.Required && String.IsNullOrEmpty(setting.DefaultValue)) result.Add(setting.Key, "required", setting.Label + " is required.");
                    continue;
                }
                ValidateValue(setting, raw!.Trim(), result);
            }

            Dictionary<string, SecretKindEnum> bound = slotKinds == null ? new Dictionary<string, SecretKindEnum>(StringComparer.Ordinal) : new Dictionary<string, SecretKindEnum>(slotKinds, StringComparer.Ordinal);
            foreach (KeyValuePair<string, SecretKindEnum> slot in bound)
            {
                SecretSlotDescriptor? descriptorSlot = descriptor.SecretSlots.FirstOrDefault(s => String.Equals(s.Name, slot.Key, StringComparison.Ordinal));
                if (descriptorSlot == null)
                {
                    result.Add(slot.Key, "unknown_slot", "Secret slot '" + slot.Key + "' is not defined by connector '" + descriptor.Type + "'.");
                    continue;
                }
                if (descriptorSlot.AcceptedKinds.Any() && !descriptorSlot.AcceptedKinds.Contains(slot.Value))
                    result.Add(slot.Key, "kind_not_accepted", descriptorSlot.Label + " accepts " + String.Join(", ", descriptorSlot.AcceptedKinds) + " secrets, not " + slot.Value + ".");
            }
            foreach (SecretSlotDescriptor slot in descriptor.SecretSlots)
            {
                if (slot.Required && !bound.ContainsKey(slot.Name)) result.Add(slot.Name, "required", slot.Label + " is required.");
            }

            return result;
        }

        /// <summary>
        /// Return a copy of the values with descriptor defaults filled in for missing or empty settings, and with
        /// unknown keys removed.
        /// </summary>
        /// <param name="descriptor">Connector descriptor.</param>
        /// <param name="values">Setting values. Null is treated as empty.</param>
        /// <returns>Values with defaults applied.</returns>
        /// <exception cref="ArgumentNullException">Thrown when descriptor is null.</exception>
        public static Dictionary<string, string> ApplyDefaults(ConnectorDescriptor descriptor, IDictionary<string, string>? values)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            Dictionary<string, string> output = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (SettingDescriptor setting in descriptor.SettingsSchema.Settings)
            {
                string? raw = null;
                if (values != null) values.TryGetValue(setting.Key, out raw);
                if (!String.IsNullOrWhiteSpace(raw)) output[setting.Key] = raw!.Trim();
                else if (!String.IsNullOrEmpty(setting.DefaultValue)) output[setting.Key] = setting.DefaultValue!;
            }
            return output;
        }

        private static void ValidateValue(SettingDescriptor setting, string value, ConnectorValidationResult result)
        {
            switch (setting.Kind)
            {
                case SettingValueKindEnum.Integer:
                    if (!Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                    {
                        result.Add(setting.Key, "invalid_format", setting.Label + " must be a whole number.");
                        return;
                    }
                    if ((setting.Minimum.HasValue && n < setting.Minimum.Value) || (setting.Maximum.HasValue && n > setting.Maximum.Value))
                        result.Add(setting.Key, "out_of_range", setting.Label + " must be between " + (setting.Minimum?.ToString(CultureInfo.InvariantCulture) ?? "any") + " and " + (setting.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "any") + ".");
                    return;
                case SettingValueKindEnum.Boolean:
                    string b = value.ToLowerInvariant();
                    if (b != "true" && b != "false" && b != "1" && b != "0" && b != "yes" && b != "no")
                        result.Add(setting.Key, "invalid_format", setting.Label + " must be true or false.");
                    return;
                case SettingValueKindEnum.Enum:
                    if (setting.AllowedValues.Any() && !setting.AllowedValues.Contains(value, StringComparer.Ordinal))
                        result.Add(setting.Key, "not_allowed", setting.Label + " must be one of: " + String.Join(", ", setting.AllowedValues) + ".");
                    return;
                case SettingValueKindEnum.StringList:
                    if (String.IsNullOrEmpty(setting.Pattern)) return;
                    foreach (string item in value.Split('\n').Select(s => s.Trim().TrimEnd('\r')).Where(s => s.Length > 0))
                    {
                        if (!MatchesPattern(setting, item, result)) return;
                    }
                    return;
                default:
                    MatchesPattern(setting, value, result);
                    return;
            }
        }

        private static bool MatchesPattern(SettingDescriptor setting, string value, ConnectorValidationResult result)
        {
            if (String.IsNullOrEmpty(setting.Pattern)) return true;
            try
            {
                if (Regex.IsMatch(value, setting.Pattern, RegexOptions.CultureInvariant, PatternTimeout)) return true;
                result.Add(setting.Key, "invalid_format", setting.Label + " does not have the expected format.");
                return false;
            }
            catch (RegexMatchTimeoutException)
            {
                result.Add(setting.Key, "invalid_format", setting.Label + " could not be validated in time.");
                return false;
            }
            catch (ArgumentException)
            {
                result.Add(setting.Key, "invalid_pattern", "Connector pattern for " + setting.Label + " is invalid.");
                return false;
            }
        }
    }
}

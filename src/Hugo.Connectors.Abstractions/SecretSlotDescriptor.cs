namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Describes a named slot through which a resource references a secret (for example <c>privateKey</c> on an SSH
    /// resource). Treat as immutable after construction.
    /// </summary>
    public class SecretSlotDescriptor
    {
        #region Public-Members

        /// <summary>
        /// Slot name, camelCase, unique within the connector. Required.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// English label. Defaults to the name.
        /// </summary>
        public string Label
        {
            get
            {
                return String.IsNullOrEmpty(_Label) ? _Name : _Label;
            }
            set
            {
                _Label = value ?? String.Empty;
            }
        }

        /// <summary>
        /// English help text.
        /// </summary>
        public string Help { get; set; } = String.Empty;

        /// <summary>
        /// Secret kinds this slot accepts. Never null; empty means any kind.
        /// </summary>
        public List<SecretKindEnum> AcceptedKinds
        {
            get
            {
                return _AcceptedKinds;
            }
            set
            {
                _AcceptedKinds = value ?? new List<SecretKindEnum>();
            }
        }

        /// <summary>
        /// Whether a secret must be bound to this slot. Default false. Cross-slot rules (for example "password or
        /// privateKey") are enforced by <see cref="IConnector.ValidateAsync"/>.
        /// </summary>
        public bool Required { get; set; } = false;

        #endregion

        #region Private-Members

        private string _Name = "secret";
        private string _Label = String.Empty;
        private List<SecretKindEnum> _AcceptedKinds = new List<SecretKindEnum>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate an empty descriptor.
        /// </summary>
        public SecretSlotDescriptor()
        {
        }

        /// <summary>
        /// Instantiate with name, label, and accepted kinds.
        /// </summary>
        /// <param name="name">Slot name.</param>
        /// <param name="label">English label.</param>
        /// <param name="kinds">Accepted secret kinds.</param>
        /// <exception cref="ArgumentNullException">Thrown when name is null or whitespace.</exception>
        public SecretSlotDescriptor(string name, string label, params SecretKindEnum[] kinds)
        {
            Name = name;
            Label = label;
            _AcceptedKinds = kinds == null ? new List<SecretKindEnum>() : new List<SecretKindEnum>(kinds);
        }

        #endregion
    }
}

namespace Hugo.Core.Profiles
{
    using System;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Models;

    /// <summary>
    /// One enabled, visible binding of a profile with its resource and connector, as the caller sees it. Not thread-safe.
    /// </summary>
    public class BoundResource
    {
        #region Public-Members

        /// <summary>
        /// Binding.
        /// </summary>
        public Binding Binding
        {
            get
            {
                return _Binding;
            }
            set
            {
                _Binding = value ?? throw new ArgumentNullException(nameof(Binding));
            }
        }

        /// <summary>
        /// Resource.
        /// </summary>
        public Resource Resource
        {
            get
            {
                return _Resource;
            }
            set
            {
                _Resource = value ?? throw new ArgumentNullException(nameof(Resource));
            }
        }

        /// <summary>
        /// Connector serving the resource.
        /// </summary>
        public IConnector Connector
        {
            get
            {
                return _Connector!;
            }
            set
            {
                _Connector = value ?? throw new ArgumentNullException(nameof(Connector));
            }
        }

        /// <summary>
        /// Access mode after the resource ceiling.
        /// </summary>
        public AccessModeEnum EffectiveAccessMode
        {
            get
            {
                AccessModeEnum mode = _Binding.AccessMode;
                if (_Resource.DefaultAccessModeCeiling.HasValue && _Resource.DefaultAccessModeCeiling.Value < mode) mode = _Resource.DefaultAccessModeCeiling.Value;
                return mode;
            }
        }

        /// <summary>
        /// Connector family (tool name prefix).
        /// </summary>
        public string Family
        {
            get
            {
                return Connector.Descriptor.Family;
            }
        }

        #endregion

        #region Private-Members

        private Binding _Binding = new Binding();
        private Resource _Resource = new Resource();
        private IConnector? _Connector = null;

        #endregion
    }
}

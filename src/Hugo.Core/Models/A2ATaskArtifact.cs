namespace Hugo.Core.Models
{
    using System;
    using Hugo.Core.Helpers;

    /// <summary>
    /// An artifact a task produced (a2a_task_artifacts, plan 7.5). <see cref="Content"/> is one of the unmanaged columns
    /// of plan 7.6. Not thread-safe.
    /// </summary>
    public class A2ATaskArtifact
    {
        #region Public-Members

        /// <summary>
        /// Row identifier.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewChildRowId() : value;
            }
        }

        /// <summary>
        /// Task identifier.
        /// </summary>
        public string TaskId
        {
            get
            {
                return _TaskId;
            }
            set
            {
                _TaskId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                _TenantId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Artifact identifier on the wire.
        /// </summary>
        public string ArtifactId
        {
            get
            {
                return _ArtifactId;
            }
            set
            {
                _ArtifactId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Name, for example result.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Media type of <see cref="Content"/>, for example text/plain or application/json.
        /// </summary>
        public string MimeType { get; set; } = "text/plain";

        /// <summary>
        /// Content.
        /// </summary>
        public string Content
        {
            get
            {
                return _Content;
            }
            set
            {
                _Content = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewChildRowId();
        private string _TaskId = String.Empty;
        private string _TenantId = String.Empty;
        private string _ArtifactId = String.Empty;
        private string _Content = String.Empty;

        #endregion
    }
}

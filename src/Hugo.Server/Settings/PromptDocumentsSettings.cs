namespace Hugo.Server.Settings
{
    using System;

    /// <summary>Managed prompt document byte, quota, retrieval and cursor limits.</summary>
    public class PromptDocumentsSettings
    {
        /// <summary>MaxPromptDocumentBytes, default 262144, minimum 1024, maximum 1048576.</summary>
        public int MaxPromptDocumentBytes { get { return _MaxPromptDocumentBytes; } set { _MaxPromptDocumentBytes = Math.Clamp(value, 1024, 1048576); } }
        private int _MaxPromptDocumentBytes = 262144;

        /// <summary>MaxPromptDocumentsPerTenant, default 256, minimum 1, maximum 10000.</summary>
        public int MaxPromptDocumentsPerTenant { get { return _MaxPromptDocumentsPerTenant; } set { _MaxPromptDocumentsPerTenant = Math.Clamp(value, 1, 10000); } }
        private int _MaxPromptDocumentsPerTenant = 256;

        /// <summary>MaxPromptDocumentStorageBytesPerTenant, default 67108864, minimum 1048576, maximum 1073741824.</summary>
        public int MaxPromptDocumentStorageBytesPerTenant { get { return _MaxPromptDocumentStorageBytesPerTenant; } set { _MaxPromptDocumentStorageBytesPerTenant = Math.Clamp(value, 1048576, 1073741824); } }
        private int _MaxPromptDocumentStorageBytesPerTenant = 67108864;

        /// <summary>MaxPromptDocumentAttachmentsPerProfile, default 32, minimum 1, maximum 128.</summary>
        public int MaxPromptDocumentAttachmentsPerProfile { get { return _MaxPromptDocumentAttachmentsPerProfile; } set { _MaxPromptDocumentAttachmentsPerProfile = Math.Clamp(value, 1, 128); } }
        private int _MaxPromptDocumentAttachmentsPerProfile = 32;

        /// <summary>MaxInlinePromptDocumentBytes, default 65536, minimum 1024, maximum 262144.</summary>
        public int MaxInlinePromptDocumentBytes { get { return _MaxInlinePromptDocumentBytes; } set { _MaxInlinePromptDocumentBytes = Math.Clamp(value, 1024, 262144); } }
        private int _MaxInlinePromptDocumentBytes = 65536;

        /// <summary>MaxPromptDocumentManifestBytes, default 16384, minimum 1024, maximum 65536.</summary>
        public int MaxPromptDocumentManifestBytes { get { return _MaxPromptDocumentManifestBytes; } set { _MaxPromptDocumentManifestBytes = Math.Clamp(value, 1024, 65536); } }
        private int _MaxPromptDocumentManifestBytes = 16384;

        /// <summary>PromptDocumentReadBytes, default 16384, minimum 1024, maximum 65536.</summary>
        public int PromptDocumentReadBytes { get { return _PromptDocumentReadBytes; } set { _PromptDocumentReadBytes = Math.Clamp(value, 1024, 65536); } }
        private int _PromptDocumentReadBytes = 16384;

        /// <summary>MaxPromptDocumentReadBytes, default 65536, minimum 1024, maximum 262144.</summary>
        public int MaxPromptDocumentReadBytes { get { return _MaxPromptDocumentReadBytes; } set { _MaxPromptDocumentReadBytes = Math.Clamp(value, 1024, 262144); } }
        private int _MaxPromptDocumentReadBytes = 65536;

        /// <summary>MaxPromptDocumentBytesPerTurn, default 262144, minimum 1024, maximum 1048576.</summary>
        public int MaxPromptDocumentBytesPerTurn { get { return _MaxPromptDocumentBytesPerTurn; } set { _MaxPromptDocumentBytesPerTurn = Math.Clamp(value, 1024, 1048576); } }
        private int _MaxPromptDocumentBytesPerTurn = 262144;

        /// <summary>PromptDocumentCursorLifetimeSeconds, default 600, minimum 30, maximum 3600.</summary>
        public int PromptDocumentCursorLifetimeSeconds { get { return _PromptDocumentCursorLifetimeSeconds; } set { _PromptDocumentCursorLifetimeSeconds = Math.Clamp(value, 30, 3600); } }
        private int _PromptDocumentCursorLifetimeSeconds = 600;

    }
}

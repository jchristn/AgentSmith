namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using Hugo.Core.Enums;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Identity, RBAC, audit, request history, and settings suites.
    /// </summary>
    public static class IdentitySuite
    {
        /// <summary>
        /// Build the suites.
        /// </summary>
        /// <returns>Suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            List<TestSuiteDescriptor> suites = new List<TestSuiteDescriptor>();

            List<TestCaseDescriptor> unit = new List<TestCaseDescriptor>();
            unit.Add(new TestCaseDescriptor("identity-unit", "token-tamper", "Tokens: tampered and expired tokens are rejected", IdentityUnitCases.TokenTamperAsync));
            unit.Add(new TestCaseDescriptor("identity-unit", "rbac-matrix", "RBAC: deny beats permit, resource scope, inheritance, Write expansion", IdentityUnitCases.RbacMatrixAsync));
            unit.Add(new TestCaseDescriptor("identity-unit", "secret-cipher", "Secrets: GCM round trip and tamper detection", IdentityUnitCases.SecretCipherAsync));
            unit.Add(new TestCaseDescriptor("identity-unit", "redaction", "Request history: secret headers and secret routes are redacted", IdentityUnitCases.RedactionAsync));
            unit.Add(new TestCaseDescriptor("identity-unit", "password-hashing", "Passwords: PBKDF2 format, salts, wrong passwords, legacy SHA-256, rehash detection", IdentityUnitCases.PasswordHashingAsync));
            unit.Add(new TestCaseDescriptor("identity-unit", "password-timing", "Passwords: constant-time comparison and unknown-user verification time", IdentityUnitCases.PasswordTimingAsync));
            suites.Add(new TestSuiteDescriptor("identity-unit", "Identity unit", unit));

            foreach (DatabaseTypeEnum provider in TestEnvironment.Providers())
            {
                string id = "identity-" + provider.ToString().ToLowerInvariant();
                List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
                cases.Add(new TestCaseDescriptor(id, "scope-map", "Every scoped route declares a resource type and operation (" + provider + ")", ct => IdentityHttpCases.ScopeMapAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "signed-request", "Signed credential requests succeed and replays are rejected (" + provider + ")", ct => IdentityHttpCases.SignedRequestAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "credential-secret-once", "Credential secret is returned once and never stored in plain text (" + provider + ")", ct => IdentityHttpCases.CredentialSecretOnceAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "owner-ceiling", "Credential grants are capped by the owning user's grants (" + provider + ")", ct => IdentityHttpCases.OwnerCeilingAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "bypass", "Bypass rules and platform-only resources (" + provider + ")", ct => IdentityHttpCases.BypassAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "credential-least-privilege", "Credentials never carry the owner's bypass flags by default; the inheritOwnerBypass opt-in is administrator-only and audited (" + provider + ")", ct => IdentityHttpCases.CredentialLeastPrivilegeAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "cross-tenant-404", "Cross-tenant reads return 404 (" + provider + ")", ct => IdentityHttpCases.CrossTenantAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "tenant-cascade", "Tenant delete cascades and protected records are kept (" + provider + ")", ct => IdentityHttpCases.TenantCascadeAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "roles-and-assignments", "Custom roles, permissions, assignments, and effective permissions (" + provider + ")", ct => IdentityHttpCases.RolesAndAssignmentsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "request-history", "Request history capture, redaction, list, and gap-filled summary (" + provider + ")", ct => IdentityHttpCases.RequestHistoryAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "audit", "Authorization denials are audited and queryable (" + provider + ")", ct => IdentityHttpCases.AuditAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "settings", "Settings metadata, validation, live cluster settings, and file writes (" + provider + ")", ct => IdentityHttpCases.SettingsAsync(provider, ct)));
                cases.Add(new TestCaseDescriptor(id, "password-upgrade", "Legacy SHA-256 password hashes are upgraded to PBKDF2 on sign-in (" + provider + ")", ct => IdentityHttpCases.PasswordUpgradeAsync(provider, ct)));
                suites.Add(new TestSuiteDescriptor(id, "Identity over HTTP (" + provider + ")", cases));
            }

            return suites;
        }
    }
}

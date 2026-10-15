namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;
    using Hugo.Core.Requests;
    using Hugo.Core.Responses;
    using Hugo.Core.Security;
    using Hugo.Core.Services;

    /// <summary>
    /// Business rules for tenants, users, credentials, roles, permissions, and assignments: validation, uniqueness,
    /// protected-record rules, cascading deletes, credential secret handling, audit, and authorization cache
    /// invalidation. Thread-safe.
    /// </summary>
    public class IdentityService
    {
        #region Public-Members

        /// <summary>
        /// Called after a tenant is created (for example to provision its default agent profile). Null does nothing.
        /// </summary>
        public Func<Tenant, CancellationToken, Task>? TenantProvisioned { get; set; } = null;

        /// <summary>
        /// Checks that a profile identifier exists in a tenant before a credential is bound to it; returns false when it
        /// does not. Null skips the check.
        /// </summary>
        public Func<string, string, CancellationToken, Task<bool>>? ProfileExists { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Database;
        private readonly SecretCipher _Cipher;
        private readonly AuditService _Audit;
        private readonly AuthorizationService _Authorization;
        private readonly Func<int> _PasswordHashIterations;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="cipher">Secret cipher.</param>
        /// <param name="audit">Audit.</param>
        /// <param name="authorization">Authorization service (cache invalidation and evaluation).</param>
        /// <param name="passwordHashIterations">Returns the PBKDF2 iterations for new password hashes (read on each use, so live setting changes apply). Null uses PasswordHasher.DefaultIterations.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public IdentityService(DatabaseDriverBase database, SecretCipher cipher, AuditService audit, AuthorizationService authorization, Func<int>? passwordHashIterations = null)
        {
            _PasswordHashIterations = passwordHashIterations ?? (() => PasswordHasher.DefaultIterations);
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            _Audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a tenant.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tenant.</returns>
        /// <exception cref="ValidationException">Thrown when the name is missing.</exception>
        /// <exception cref="ConflictException">Thrown when the name is taken.</exception>
        public async Task<Tenant> CreateTenantAsync(RequestContext ctx, TenantRequest request, CancellationToken token = default)
        {
            if (request == null || String.IsNullOrWhiteSpace(request.Name)) throw Invalid("name", "A tenant name is required.");
            if (await _Database.Tenants.ReadByNameAsync(request.Name.Trim(), token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A tenant named '" + request.Name + "' already exists.");
            Tenant tenant = new Tenant();
            tenant.Name = request.Name.Trim();
            tenant.Description = request.Description;
            tenant.Active = request.Active ?? true;
            tenant.Region = request.Region;
            tenant.ParentTenantId = request.ParentTenantId;
            tenant.DefaultAgentProfileId = request.DefaultAgentProfileId;
            await _Database.Tenants.CreateAsync(tenant, token).ConfigureAwait(false);
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.ConfigurationChanged, "Tenant", tenant.Id, "Tenant created.", tenant.Id, token).ConfigureAwait(false);
            Func<Tenant, CancellationToken, Task>? provisioned = TenantProvisioned;
            if (provisioned != null)
            {
                await provisioned(tenant, token).ConfigureAwait(false);
                tenant = await _Database.Tenants.ReadAsync(tenant.Id, token).ConfigureAwait(false) ?? tenant;
            }
            return tenant;
        }

        /// <summary>
        /// Update a tenant.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Tenant.</returns>
        /// <exception cref="NotFoundException">Thrown when the tenant does not exist.</exception>
        public async Task<Tenant> UpdateTenantAsync(RequestContext ctx, string tenantId, TenantRequest request, CancellationToken token = default)
        {
            Tenant tenant = await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false) ?? throw new NotFoundException("Tenant not found.");
            if (request == null) throw Invalid("body", "A request body is required.");
            if (!String.IsNullOrWhiteSpace(request.Name) && request.Name.Trim() != tenant.Name)
            {
                if (await _Database.Tenants.ReadByNameAsync(request.Name.Trim(), token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A tenant named '" + request.Name + "' already exists.");
                tenant.Name = request.Name.Trim();
            }
            if (request.Description != null) tenant.Description = request.Description;
            if (request.Active.HasValue)
            {
                if (tenant.IsProtected && !request.Active.Value) throw new ConflictException("Protected", "The protected tenant cannot be deactivated.");
                tenant.Active = request.Active.Value;
            }
            if (request.Region != null) tenant.Region = request.Region.Length == 0 ? null : request.Region;
            if (request.ParentTenantId != null) tenant.ParentTenantId = request.ParentTenantId.Length == 0 ? null : request.ParentTenantId;
            if (request.DefaultAgentProfileId != null) tenant.DefaultAgentProfileId = request.DefaultAgentProfileId.Length == 0 ? null : request.DefaultAgentProfileId;
            string summary = "Tenant updated.";
            await _Database.Tenants.UpdateAsync(tenant, token).ConfigureAwait(false);
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.ConfigurationChanged, "Tenant", tenant.Id, summary, tenant.Id, token).ConfigureAwait(false);
            _Authorization.Invalidate();
            return tenant;
        }

        /// <summary>
        /// Delete a tenant and every tenant-owned record.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows deleted.</returns>
        /// <exception cref="NotFoundException">Thrown when the tenant does not exist.</exception>
        /// <exception cref="ConflictException">Thrown when the tenant is protected.</exception>
        public async Task<int> DeleteTenantAsync(RequestContext ctx, string tenantId, CancellationToken token = default)
        {
            Tenant tenant = await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false) ?? throw new NotFoundException("Tenant not found.");
            if (tenant.IsProtected) throw new ConflictException("Protected", "The protected tenant cannot be deleted.");
            int deleted = await _Database.Tenants.DeleteCascadeAsync(tenantId, token).ConfigureAwait(false);
            _Authorization.Invalidate();
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.ConfigurationChanged, "Tenant", tenantId, "Tenant deleted with " + deleted + " rows.", null, token).ConfigureAwait(false);
            return deleted;
        }

        /// <summary>
        /// Create a user and grant TenantMember.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>User.</returns>
        /// <exception cref="ValidationException">Thrown when email or password is missing.</exception>
        /// <exception cref="ConflictException">Thrown when the email is taken in the tenant.</exception>
        public async Task<User> CreateUserAsync(RequestContext ctx, string tenantId, UserRequest request, CancellationToken token = default)
        {
            if (request == null) throw Invalid("body", "A request body is required.");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (String.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@')) errors["email"] = "A valid email is required.";
            if (String.IsNullOrEmpty(request.Password) || request.Password.Length < 8) errors["password"] = "A password of at least 8 characters is required.";
            if (request.IsAdmin == true && !ctx.IsAdmin) errors["isAdmin"] = "Only system administrators may create system administrators.";
            if (errors.Count > 0) throw new ValidationException("The user is invalid.", errors);
            await RequireTenantAsync(tenantId, token).ConfigureAwait(false);
            if (await _Database.Users.ReadByEmailAsync(tenantId, request.Email!.Trim(), token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A user with that email already exists in the tenant.");

            User user = new User();
            user.TenantId = tenantId;
            user.Email = request.Email.Trim();
            user.PasswordHash = PasswordHasher.Hash(request.Password!, _PasswordHashIterations());
            user.FirstName = request.FirstName;
            user.LastName = request.LastName;
            user.IsTenantAdmin = request.IsTenantAdmin ?? false;
            user.IsAdmin = request.IsAdmin ?? false;
            user.Active = request.Active ?? true;
            user.MustChangePassword = request.MustChangePassword ?? false;
            user.PreferredLocale = request.PreferredLocale;
            user.DefaultAgentProfileId = request.DefaultAgentProfileId;
            await _Database.Users.CreateAsync(user, token).ConfigureAwait(false);

            BuiltInRole member = BuiltInRoles.Find(BuiltInRoles.TenantMember)!;
            UserRoleAssignment assignment = new UserRoleAssignment();
            assignment.TenantId = tenantId;
            assignment.UserId = user.Id;
            assignment.RoleId = member.Id;
            assignment.RoleName = member.Name;
            await _Database.UserRoleAssignments.CreateAsync(assignment, token).ConfigureAwait(false);

            _Authorization.Invalidate();
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.ConfigurationChanged, "User", user.Id, "User " + user.Email + " created.", tenantId, token).ConfigureAwait(false);
            return user;
        }

        /// <summary>
        /// Update a user. Omitted fields are unchanged.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">User identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>User.</returns>
        /// <exception cref="NotFoundException">Thrown when the user does not exist.</exception>
        public async Task<User> UpdateUserAsync(RequestContext ctx, string tenantId, string id, UserRequest request, CancellationToken token = default)
        {
            User user = await _Database.Users.ReadAsync(tenantId, id, token).ConfigureAwait(false) ?? throw new NotFoundException("User not found.");
            if (request == null) throw Invalid("body", "A request body is required.");
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (request.Email != null && (String.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))) errors["email"] = "A valid email is required.";
            if (request.Password != null && request.Password.Length < 8) errors["password"] = "Passwords must be at least 8 characters.";
            if (request.IsAdmin.HasValue && request.IsAdmin.Value != user.IsAdmin && !ctx.IsAdmin) errors["isAdmin"] = "Only system administrators may change the system administrator flag.";
            if (errors.Count > 0) throw new ValidationException("The user is invalid.", errors);

            if (request.Email != null && !String.Equals(request.Email.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
            {
                if (await _Database.Users.ReadByEmailAsync(tenantId, request.Email.Trim(), token).ConfigureAwait(false) != null) throw new ConflictException("Duplicate", "A user with that email already exists in the tenant.");
                user.Email = request.Email.Trim();
            }
            if (request.Password != null)
            {
                user.PasswordHash = PasswordHasher.Hash(request.Password, _PasswordHashIterations());
                user.MustChangePassword = request.MustChangePassword ?? (ctx.UserId != user.Id);
            }
            else if (request.MustChangePassword.HasValue)
            {
                user.MustChangePassword = request.MustChangePassword.Value;
            }
            if (request.PreferredLocale != null) user.PreferredLocale = request.PreferredLocale.Length == 0 ? null : request.PreferredLocale;
            if (request.DefaultAgentProfileId != null) user.DefaultAgentProfileId = request.DefaultAgentProfileId.Length == 0 ? null : request.DefaultAgentProfileId;
            if (request.FirstName != null) user.FirstName = request.FirstName;
            if (request.LastName != null) user.LastName = request.LastName;
            if (request.IsTenantAdmin.HasValue) user.IsTenantAdmin = request.IsTenantAdmin.Value;
            if (request.IsAdmin.HasValue) user.IsAdmin = request.IsAdmin.Value;
            if (request.Active.HasValue)
            {
                if (user.IsProtected && !request.Active.Value) throw new ConflictException("Protected", "Protected users cannot be deactivated.");
                user.Active = request.Active.Value;
            }
            await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);
            _Authorization.Invalidate();
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.ConfigurationChanged, "User", user.Id, "User updated.", tenantId, token).ConfigureAwait(false);
            return user;
        }

        /// <summary>
        /// Change a user's password. Users changing their own password must supply the current one, and the
        /// must-change-password flag is cleared; an administrator resetting another user's password sets the flag unless
        /// told otherwise. Other sessions of the user are revoked.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">User identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="mustChangePassword">Flag for an administrator reset; null sets it when the target is someone else.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated user.</returns>
        /// <exception cref="NotFoundException">Thrown when the user does not exist.</exception>
        /// <exception cref="ValidationException">Thrown when the current password is wrong or the new password is invalid.</exception>
        public async Task<User> ChangePasswordAsync(RequestContext ctx, string tenantId, string id, PasswordChangeRequest request, bool? mustChangePassword = null, CancellationToken token = default)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (request == null) throw Invalid("body", "A request body is required.");
            User user = await _Database.Users.ReadAsync(tenantId, id, token).ConfigureAwait(false) ?? throw new NotFoundException("User not found.");
            bool self = ctx.PrincipalType == PrincipalTypeEnum.User && ctx.UserId == user.Id;
            Dictionary<string, string> errors = new Dictionary<string, string>();
            Dictionary<string, string> codes = new Dictionary<string, string>();
            if (self && !PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            {
                errors["currentPassword"] = "The current password is incorrect.";
                codes["currentPassword"] = "InvalidPassword";
            }
            string next = request.NewPassword ?? String.Empty;
            if (next.Length < 8 || next.Length > 256)
            {
                errors["newPassword"] = "Passwords must be 8 to 256 characters.";
                codes["newPassword"] = "InvalidLength";
            }
            else if (PasswordHasher.Verify(next, user.PasswordHash))
            {
                errors["newPassword"] = "The new password must differ from the current one.";
                codes["newPassword"] = "Unchanged";
            }
            if (errors.Count > 0) throw new ValidationException("The password change is invalid.", errors, codes);

            user.PasswordHash = PasswordHasher.Hash(next, _PasswordHashIterations());
            user.MustChangePassword = self ? false : (mustChangePassword ?? true);
            await _Database.Users.UpdateAsync(user, token).ConfigureAwait(false);

            EnumerationQuery query = new EnumerationQuery();
            query.MaxResults = 1000;
            SessionFilter userSessions = new SessionFilter();
            userSessions.UserId = user.Id;
            EnumerationResult<AuthSession> sessions = await _Database.Sessions.EnumerateAsync(tenantId, query, userSessions, token).ConfigureAwait(false);
            foreach (AuthSession session in sessions.Objects.Where(x => x.UserId == user.Id && x.Id != ctx.SessionId && !x.RevokedUtc.HasValue))
            {
                await _Database.Sessions.RevokeAsync(session.Id, "Password changed.", token).ConfigureAwait(false);
            }
            _Authorization.Invalidate();
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.PasswordChanged, "User", user.Id, (self ? "Password changed by the user " : "Password reset for ") + user.Email + ".", tenantId, token).ConfigureAwait(false);
            return user;
        }

        /// <summary>
        /// Delete a user with their credentials, sessions, and assignments.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="NotFoundException">Thrown when the user does not exist.</exception>
        /// <exception cref="ConflictException">Thrown when the user is protected.</exception>
        public async Task DeleteUserAsync(RequestContext ctx, string tenantId, string id, CancellationToken token = default)
        {
            User user = await _Database.Users.ReadAsync(tenantId, id, token).ConfigureAwait(false) ?? throw new NotFoundException("User not found.");
            if (user.IsProtected) throw new ConflictException("Protected", "Protected users cannot be deleted.");
            if (user.Id == ctx.UserId) throw new ConflictException("SelfDelete", "You cannot delete your own account.");
            await _Database.Users.DeleteCascadeAsync(tenantId, id, token).ConfigureAwait(false);
            _Authorization.Invalidate();
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.ConfigurationChanged, "User", id, "User " + user.Email + " deleted.", tenantId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a credential. The raw secret is returned only here.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credential and secret.</returns>
        /// <exception cref="ValidationException">Thrown when the request is invalid.</exception>
        public async Task<CredentialCreateResponse> CreateCredentialAsync(RequestContext ctx, string tenantId, CredentialRequest request, CancellationToken token = default)
        {
            if (request == null || String.IsNullOrWhiteSpace(request.Name)) throw Invalid("name", "A credential name is required.");
            string? userId = String.IsNullOrEmpty(request.UserId) ? ctx.UserId : request.UserId;
            if (String.IsNullOrEmpty(userId)) throw Invalid("userId", "An owning user is required.");
            User owner = await _Database.Users.ReadAsync(tenantId, userId, token).ConfigureAwait(false) ?? throw Invalid("userId", "The owning user does not exist in this tenant.");

            BuiltInRole? builtIn = null;
            Role? custom = null;
            if (!String.IsNullOrEmpty(request.RoleName))
            {
                builtIn = BuiltInRoles.Find(request.RoleName);
                if (builtIn == null) custom = await _Database.Roles.ReadByNameAsync(tenantId, request.RoleName, token).ConfigureAwait(false);
                if (builtIn == null && custom == null) throw Invalid("roleName", "Unknown role '" + request.RoleName + "'.");
            }

            string secret = SecretGenerator.NewSecretKey();
            Credential credential = new Credential();
            credential.TenantId = tenantId;
            credential.UserId = owner.Id;
            credential.Name = request.Name.Trim();
            credential.AccessKey = SecretGenerator.NewAccessKey();
            credential.SecretKeyEncrypted = _Cipher.EncryptCredential(secret, credential.Id);
            credential.SecretKeyLast4 = SecretGenerator.Last4(secret);
            credential.AuthMode = request.AuthMode ?? CredentialAuthModeEnum.Hybrid;
            credential.ExpiresUtc = request.ExpiresUtc;
            credential.Active = request.Active ?? true;
            if (request.InheritOwnerBypass == true)
            {
                RequireBypassAdministrator(ctx);
                credential.InheritOwnerBypass = true;
            }
            await ApplyProfileBindingAsync(credential, tenantId, request, token).ConfigureAwait(false);
            await _Database.Credentials.CreateAsync(credential, token).ConfigureAwait(false);

            if (builtIn != null || custom != null)
            {
                CredentialScopeAssignment scope = new CredentialScopeAssignment();
                scope.TenantId = tenantId;
                scope.CredentialId = credential.Id;
                scope.RoleId = builtIn != null ? builtIn.Id : custom!.Id;
                scope.RoleName = builtIn != null ? builtIn.Name : custom!.Name;
                await _Database.CredentialScopeAssignments.CreateAsync(scope, token).ConfigureAwait(false);
            }

            _Authorization.Invalidate();
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.CredentialCreated, "Credential", credential.Id, "Credential " + credential.Name + " created for " + owner.Email + ".", tenantId, token).ConfigureAwait(false);
            if (credential.InheritOwnerBypass) await AuditBypassAsync(ctx, credential, owner, tenantId, token).ConfigureAwait(false);

            CredentialCreateResponse response = new CredentialCreateResponse();
            response.Credential = credential;
            response.AccessKey = credential.AccessKey;
            response.SecretKey = secret;
            return response;
        }

        /// <summary>
        /// Update a credential's name, auth mode, expiry, or active flag.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Credential identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credential.</returns>
        /// <exception cref="NotFoundException">Thrown when the credential does not exist.</exception>
        public async Task<Credential> UpdateCredentialAsync(RequestContext ctx, string tenantId, string id, CredentialRequest request, CancellationToken token = default)
        {
            Credential credential = await _Database.Credentials.ReadAsync(tenantId, id, token).ConfigureAwait(false) ?? throw new NotFoundException("Credential not found.");
            if (request == null) throw Invalid("body", "A request body is required.");
            if (!String.IsNullOrWhiteSpace(request.Name)) credential.Name = request.Name.Trim();
            if (request.AuthMode.HasValue) credential.AuthMode = request.AuthMode.Value;
            if (request.ExpiresUtc.HasValue) credential.ExpiresUtc = request.ExpiresUtc;
            if (request.Active.HasValue)
            {
                if (credential.IsProtected && !request.Active.Value) throw new ConflictException("Protected", "Protected credentials cannot be deactivated.");
                credential.Active = request.Active.Value;
            }
            bool bypassChanged = request.InheritOwnerBypass.HasValue && request.InheritOwnerBypass.Value != credential.InheritOwnerBypass;
            if (bypassChanged)
            {
                RequireBypassAdministrator(ctx);
                credential.InheritOwnerBypass = request.InheritOwnerBypass!.Value;
            }
            await ApplyProfileBindingAsync(credential, tenantId, request, token).ConfigureAwait(false);
            await _Database.Credentials.UpdateAsync(credential, token).ConfigureAwait(false);
            _Authorization.Invalidate();
            if (bypassChanged)
            {
                User? owner = await _Database.Users.ReadAsync(tenantId, credential.UserId, token).ConfigureAwait(false);
                await AuditBypassAsync(ctx, credential, owner, tenantId, token).ConfigureAwait(false);
            }
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.ConfigurationChanged, "Credential", credential.Id, "Credential updated" + (String.IsNullOrEmpty(credential.AgentProfileId) ? "." : " (agent profile " + credential.AgentProfileId + (credential.LockToProfile ? ", locked" : String.Empty) + ")."), tenantId, token).ConfigureAwait(false);
            return credential;
        }

        /// <summary>
        /// Apply a request's agent profile binding to a credential (P5.18, D-018): an empty profile identifier clears the
        /// binding and the lock; a lock without a bound profile is refused.
        /// </summary>
        /// <param name="credential">Credential.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ValidationException">Thrown when the profile does not exist or a lock has no profile.</exception>
        public async Task ApplyProfileBindingAsync(Credential credential, string tenantId, CredentialRequest request, CancellationToken token = default)
        {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            if (request == null) return;
            if (request.AgentProfileId != null)
            {
                string id = request.AgentProfileId.Trim();
                if (id.Length == 0)
                {
                    credential.AgentProfileId = null;
                    credential.LockToProfile = false;
                }
                else
                {
                    Func<string, string, CancellationToken, Task<bool>>? exists = ProfileExists;
                    if (exists != null && !await exists(tenantId, id, token).ConfigureAwait(false)) throw Invalid("agentProfileId", "Agent profile " + id + " does not exist in this tenant.");
                    credential.AgentProfileId = id;
                }
            }
            if (request.LockToProfile.HasValue)
            {
                if (request.LockToProfile.Value && String.IsNullOrEmpty(credential.AgentProfileId)) throw Invalid("lockToProfile", "A credential can be locked only to a bound agent profile; set agentProfileId too.");
                credential.LockToProfile = request.LockToProfile.Value;
            }
        }

        /// <summary>
        /// Rotate a credential's secret and revoke its sessions. The new secret is returned only here.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Credential and new secret.</returns>
        /// <exception cref="NotFoundException">Thrown when the credential does not exist.</exception>
        public async Task<CredentialCreateResponse> RotateCredentialAsync(RequestContext ctx, string tenantId, string id, CancellationToken token = default)
        {
            Credential credential = await _Database.Credentials.ReadAsync(tenantId, id, token).ConfigureAwait(false) ?? throw new NotFoundException("Credential not found.");
            string secret = SecretGenerator.NewSecretKey();
            credential.SecretKeyEncrypted = _Cipher.EncryptCredential(secret, credential.Id);
            credential.SecretKeyLast4 = SecretGenerator.Last4(secret);
            await _Database.Credentials.UpdateAsync(credential, token).ConfigureAwait(false);
            await _Database.Sessions.DeleteByCredentialAsync(credential.Id, token).ConfigureAwait(false);
            // The old secret and the deleted sessions stop working at once on every replica (P10.24 authentication cache).
            _Authorization.Invalidate();
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.CredentialRotated, "Credential", credential.Id, "Credential secret rotated.", tenantId, token).ConfigureAwait(false);

            CredentialCreateResponse response = new CredentialCreateResponse();
            response.Credential = credential;
            response.AccessKey = credential.AccessKey;
            response.SecretKey = secret;
            return response;
        }

        /// <summary>
        /// Delete a credential with its scope assignments and sessions.
        /// </summary>
        /// <param name="ctx">Caller.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="NotFoundException">Thrown when the credential does not exist.</exception>
        /// <exception cref="ConflictException">Thrown when the credential is protected.</exception>
        public async Task DeleteCredentialAsync(RequestContext ctx, string tenantId, string id, CancellationToken token = default)
        {
            Credential credential = await _Database.Credentials.ReadAsync(tenantId, id, token).ConfigureAwait(false) ?? throw new NotFoundException("Credential not found.");
            if (credential.IsProtected) throw new ConflictException("Protected", "Protected credentials cannot be deleted.");
            await DeleteCredentialCascadeAsync(tenantId, id, token).ConfigureAwait(false);
            _Authorization.Invalidate();
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.ConfigurationChanged, "Credential", id, "Credential " + credential.Name + " deleted.", tenantId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Validate a permission's resource and operation lists.
        /// </summary>
        /// <param name="resourceTypes">Resource type names.</param>
        /// <param name="operations">Operation names.</param>
        /// <exception cref="ValidationException">Thrown when a name is unknown or a list is empty.</exception>
        public static void ValidatePermissionLists(List<string>? resourceTypes, List<string>? operations)
        {
            Dictionary<string, string> errors = new Dictionary<string, string>();
            if (resourceTypes == null || resourceTypes.Count == 0) errors["resourceTypes"] = "At least one resource type is required.";
            else
            {
                List<string> bad = resourceTypes.Where(r => !Enum.TryParse<ResourceTypeEnum>(r, true, out ResourceTypeEnum _)).ToList();
                if (bad.Count > 0) errors["resourceTypes"] = "Unknown resource types: " + String.Join(", ", bad) + ".";
            }
            if (operations == null || operations.Count == 0) errors["operationTypes"] = "At least one operation is required.";
            else
            {
                List<string> bad = operations.Where(o => !Enum.TryParse<OperationTypeEnum>(o, true, out OperationTypeEnum _)).ToList();
                if (bad.Count > 0) errors["operationTypes"] = "Unknown operations: " + String.Join(", ", bad) + ".";
            }
            if (errors.Count > 0) throw new ValidationException("The permission is invalid.", errors);
        }

        /// <summary>
        /// Resolve a role by identifier or name for an assignment (tenant roles first, then built-ins).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="roleId">Role identifier.</param>
        /// <param name="roleName">Role name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Role.</returns>
        /// <exception cref="ValidationException">Thrown when the role cannot be resolved.</exception>
        public async Task<Role> ResolveRoleAsync(string tenantId, string? roleId, string? roleName, CancellationToken token = default)
        {
            Role? role = null;
            if (!String.IsNullOrEmpty(roleId)) role = await _Database.Roles.ReadAsync(tenantId, roleId, token).ConfigureAwait(false);
            if (role == null && !String.IsNullOrEmpty(roleName)) role = await _Database.Roles.ReadByNameAsync(tenantId, roleName, token).ConfigureAwait(false);
            if (role == null) throw Invalid("roleId", "The role does not exist.");
            return role;
        }

        /// <summary>
        /// Compute effective permissions for a user.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="userId">User identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Effective permissions.</returns>
        /// <exception cref="NotFoundException">Thrown when the user does not exist.</exception>
        public async Task<EffectivePermissionsResponse> EffectiveForUserAsync(string tenantId, string userId, CancellationToken token = default)
        {
            User user = await _Database.Users.ReadAsync(tenantId, userId, token).ConfigureAwait(false) ?? throw new NotFoundException("User not found.");
            RequestContext principal = new RequestContext();
            principal.IsAuthenticated = true;
            principal.PrincipalType = PrincipalTypeEnum.User;
            principal.TenantId = tenantId;
            principal.UserId = user.Id;
            principal.IsAdmin = user.IsAdmin;
            principal.IsTenantAdmin = user.IsTenantAdmin;
            List<PermissionGrant> grants = await _Authorization.GrantsForUserAsync(tenantId, user.Id, token).ConfigureAwait(false);
            return await BuildEffectiveAsync(principal, user.Id, grants, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Compute effective permissions for a credential, including the owner ceiling.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="credentialId">Credential identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Effective permissions.</returns>
        /// <exception cref="NotFoundException">Thrown when the credential does not exist.</exception>
        public async Task<EffectivePermissionsResponse> EffectiveForCredentialAsync(string tenantId, string credentialId, CancellationToken token = default)
        {
            Credential credential = await _Database.Credentials.ReadAsync(tenantId, credentialId, token).ConfigureAwait(false) ?? throw new NotFoundException("Credential not found.");
            User? owner = await _Database.Users.ReadAsync(tenantId, credential.UserId, token).ConfigureAwait(false);
            RequestContext principal = new RequestContext();
            principal.IsAuthenticated = true;
            principal.PrincipalType = PrincipalTypeEnum.Credential;
            principal.TenantId = tenantId;
            principal.CredentialId = credential.Id;
            principal.UserId = credential.UserId;
            principal.User = owner;
            principal.IsAdmin = credential.InheritOwnerBypass && owner != null && owner.IsAdmin;
            principal.IsTenantAdmin = credential.InheritOwnerBypass && owner != null && owner.IsTenantAdmin;
            List<PermissionGrant> grants = await _Authorization.GrantsForCredentialAsync(tenantId, credential.Id, token).ConfigureAwait(false);
            return await BuildEffectiveAsync(principal, credential.Id, grants, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void RequireBypassAdministrator(RequestContext ctx)
        {
            // Only an interactive tenant or system administrator may let a credential carry its owner's bypass; a
            // credential can never grant it (not even one that carries the bypass itself).
            bool administrator = ctx.PrincipalType == PrincipalTypeEnum.Administrator
                || (ctx.PrincipalType == PrincipalTypeEnum.User && (ctx.IsAdmin || ctx.IsTenantAdmin));
            if (!administrator) throw new ForbiddenException("BypassRequiresAdministrator", "Only a signed-in tenant or system administrator can change inheritOwnerBypass.");
        }

        private async Task AuditBypassAsync(RequestContext ctx, Credential credential, User? owner, string tenantId, CancellationToken token)
        {
            string ownerFlags = owner == null ? "owner not found" : (owner.IsAdmin ? "owner is a system administrator" : owner.IsTenantAdmin ? "owner is a tenant administrator" : "owner holds no bypass, so this has no effect");
            string description = "Credential " + credential.Name + (credential.InheritOwnerBypass ? " now carries" : " no longer carries") + " its owner's administrator bypass (" + ownerFlags + ").";
            await _Audit.WriteAsync(ctx, AuditEventTypeEnum.CredentialBypassChanged, "Credential", credential.Id, description, tenantId, token).ConfigureAwait(false);
        }

        private async Task<EffectivePermissionsResponse> BuildEffectiveAsync(RequestContext principal, string principalId, List<PermissionGrant> grants, CancellationToken token)
        {
            EffectivePermissionsResponse response = new EffectivePermissionsResponse();
            response.PrincipalType = principal.PrincipalType;
            response.PrincipalId = principalId;
            response.TenantId = principal.TenantId!;
            response.IsAdmin = principal.IsAdmin;
            response.IsTenantAdmin = principal.IsTenantAdmin;
            foreach (PermissionGrant grant in grants)
            {
                EffectiveGrant g = new EffectiveGrant();
                g.Source = grant.Source;
                g.PermissionType = grant.PermissionType;
                g.ResourceTypes = grant.ResourceTypes;
                g.Operations = grant.Operations;
                g.Scope = grant.Scope;
                g.ResourceId = grant.ResourceId;
                g.InheritsToChildren = grant.InheritsToChildren;
                response.Grants.Add(g);
            }

            OperationTypeEnum[] operations = new OperationTypeEnum[] { OperationTypeEnum.Read, OperationTypeEnum.Create, OperationTypeEnum.Update, OperationTypeEnum.Delete, OperationTypeEnum.Execute, OperationTypeEnum.Admin };
            foreach (ResourceTypeEnum resource in Enum.GetValues<ResourceTypeEnum>())
            {
                if (resource == ResourceTypeEnum.All) continue;
                foreach (OperationTypeEnum operation in operations)
                {
                    AuthorizationDecision decision = await _Authorization.EvaluateAsync(principal, principal.TenantId, resource, operation, null, token).ConfigureAwait(false);
                    PermissionCheck check = new PermissionCheck();
                    check.ResourceType = resource;
                    check.Operation = operation;
                    check.Permitted = decision.Permitted;
                    response.HasPermission.Add(check);
                }
            }
            return response;
        }

        private async Task DeleteCredentialCascadeAsync(string tenantId, string credentialId, CancellationToken token)
        {
            await _Database.Credentials.DeleteCascadeAsync(tenantId, credentialId, token).ConfigureAwait(false);
        }

        private async Task RequireTenantAsync(string tenantId, CancellationToken token)
        {
            if (await _Database.Tenants.ReadAsync(tenantId, token).ConfigureAwait(false) == null) throw new NotFoundException("Tenant not found.");
        }

        private static ValidationException Invalid(string field, string message)
        {
            return new ValidationException(message, new Dictionary<string, string> { { field, message } });
        }

        #endregion
    }
}

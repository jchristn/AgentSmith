namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Core.Database;
    using Hugo.Core.Database.Ado;
    using Hugo.Core.Enums;
    using Hugo.Core.Enumeration;
    using Hugo.Core.Responses;
    using Hugo.Core.Models;
    using Hugo.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>Real-provider document revision, attachment concurrency and tenant-isolation contracts.</summary>
    public static class AgentDocumentsPersistenceSuite
    {
        /// <summary>Build provider-specific persistence tests.</summary>
        /// <returns>Independent synthetic-data suites.</returns>
        public static List<TestSuiteDescriptor> Build()
        {
            return TestEnvironment.Providers().Select(provider =>
            {
                string id = "agent-documents-persistence-" + provider.ToString().ToLowerInvariant();
                return new TestSuiteDescriptor(id, "Versioned agent document persistence", new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(id, "upgrade", "Version 11 upgrade preserves profiles; failed DDL rolls back and retries safely", ct => UpgradeAsync(provider, ct)),
                    new TestCaseDescriptor(id, "canonicalization", "Canonical UTF-8 fidelity, normalized uniqueness and immutable history", ct => CanonicalizationAsync(provider, ct)),
                    new TestCaseDescriptor(id, "cas", "Concurrent revision writers have one winner; no-op and stale writes preserve state", ct => CasAsync(provider, ct)),
                    new TestCaseDescriptor(id, "attachments", "Profile attachment row/set CAS, exact reorder and deletion lifecycle", ct => AttachmentsAsync(provider, ct)),
                    new TestCaseDescriptor(id, "pin-collision", "Pinned revision names cannot collide with Latest names after attach or rename", ct => PinCollisionAsync(provider, ct)),
                    new TestCaseDescriptor(id, "isolation-limits", "Tenant-qualified relations and storage quotas reject invalid writes atomically", ct => IsolationLimitsAsync(provider, ct)),
                    new TestCaseDescriptor(id, "tenant-quotas", "Tenant count/history quotas serialize competing writes and release storage on deletion", ct => TenantQuotasAsync(provider, ct)),
                    new TestCaseDescriptor(id, "public-edit-guard", "Public Latest content authorization is enforced inside the revision transaction", ct => PublicEditGuardAsync(provider, ct)),
                    new TestCaseDescriptor(id, "parent-delete-race", "Concurrent parent deletion cannot leave orphan documents or attachments", ct => ParentDeleteRaceAsync(provider, ct)),
                    new TestCaseDescriptor(id, "version-exhaustion", "Maximum JSON-safe versions allow no-ops and reject increments atomically without wrapping", ct => VersionExhaustionAsync(provider, ct)),
                    new TestCaseDescriptor(id, "revocation", "Attachment and document state changes revoke visitor revisions without profile overwrites", ct => RevocationAsync(provider, ct))
                });
            }).ToList();
        }

        private static async Task UpgradeAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            DatabaseSettings settings = TestEnvironment.IsolatedSettings(provider);
            string profileId;
            using (DatabaseDriverBase database = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, token).ConfigureAwait(false))
            {
                await database.Tenants.CreateAsync(new Tenant { Id = "documents-tenant", Name = "Documents upgrade" }, token).ConfigureAwait(false);
                profileId = (await ProfileAsync(database, token).ConfigureAwait(false)).Id;
                AdoDatabaseDriver ado = (AdoDatabaseDriver)database;
                foreach (string table in new[] { "agent_prompt_documents", "prompt_document_revisions", "prompt_documents" }) await ado.NonQueryAsync("DROP TABLE " + ado.Table(table) + ";", null, token).ConfigureAwait(false);
                await ado.NonQueryAsync("ALTER TABLE " + ado.Table("agent_profiles") + " DROP COLUMN document_set_version;", null, token).ConfigureAwait(false);
                await ado.NonQueryAsync("DELETE FROM " + ado.Table("schema_migrations") + " WHERE version=12;", null, token).ConfigureAwait(false);
                await ado.NonQueryAsync("CREATE TABLE " + ado.Table("prompt_documents") + " (broken INTEGER);", null, token).ConfigureAwait(false);
                bool failed = false;
                try { await ado.InitializeAsync(token).ConfigureAwait(false); }
                catch (Exception e) when (e is System.Data.Common.DbException) { failed = true; }
                Assert.True(failed, "Conflicting DDL fails upgrade");
                object? count = await ado.ScalarAsync("SELECT COUNT(*) FROM " + ado.Table("schema_migrations") + " WHERE version=12;", null, token).ConfigureAwait(false);
                Assert.Equal(0L, Convert.ToInt64(count), "Failed migration is not recorded");
                await ado.NonQueryAsync("DROP TABLE " + ado.Table("prompt_documents") + ";", null, token).ConfigureAwait(false);
            }
            using (DatabaseDriverBase upgraded = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, token).ConfigureAwait(false))
            {
                AgentProfile profile = (await upgraded.AgentProfiles.ReadAsync("documents-tenant", profileId, token).ConfigureAwait(false))!;
                Assert.Equal(0L, profile.DocumentSetVersion, "Existing profile gets zero set version");
                PromptDocument d = await CreateAsync(upgraded, "Upgrade.md", "persist after restart", token).ConfigureAwait(false);
                await upgraded.AgentPromptDocuments.CreateAsync(Attachment(profile, d), 0, token).ConfigureAwait(false);
            }
            using (DatabaseDriverBase restarted = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, token).ConfigureAwait(false))
            {
                Assert.Equal(0, restarted.MigrationsApplied, "Repeated startup applies no migrations");
                AgentPromptDocument a = (await restarted.AgentPromptDocuments.ListAsync("documents-tenant", profileId, token).ConfigureAwait(false)).Single();
                Assert.Equal("persist after restart", (await restarted.PromptDocuments.ReadRevisionAsync(a.TenantId, a.DocumentId, 1, token).ConfigureAwait(false))!.Markdown, "Revision and attachment survive restart");
            }
        }

        private static async Task CanonicalizationAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await DatabaseAsync(provider, token).ConfigureAwait(false))
            {
                PromptDocument d = await CreateAsync(db, "Schema-e\u0301.md", "\uFEFF# Schema\r\n{{literal}} 世界  \r\n", token).ConfigureAwait(false);
                PromptDocumentRevision r = (await db.PromptDocuments.ReadRevisionAsync(d.TenantId, d.Id, 1, token).ConfigureAwait(false))!;
                Assert.Equal("# Schema\n{{literal}} 世界  \n", r.Markdown, "Canonical content preserves meaningful whitespace and braces");
                Assert.Equal(Encoding.UTF8.GetByteCount(r.Markdown), r.ContentBytes, "Canonical byte count");
                Assert.True(r.ContentHash.Length == 64 && d.Name.IsNormalized(), "Hash and normalized name");
                await RejectAsync<ConflictException>(() => CreateAsync(db, "SCHEMA-É.MD", "duplicate", token)).ConfigureAwait(false);
                d.Description = "Updated retrieval trigger";
                Assert.True(await db.PromptDocuments.TryUpdateAsync(d, null, 1, token).ConfigureAwait(false), "Metadata snapshot write");
                Assert.Equal(2, d.CurrentRevision, "Description change creates revision");
                PromptDocumentRevision original = (await db.PromptDocuments.ReadRevisionAsync(d.TenantId, d.Id, 1, token).ConfigureAwait(false))!;
                Assert.Equal("Read the schema before use.", original.Description, "Historical description immutable");
                Assert.Equal(r.Markdown, (await db.PromptDocuments.ReadRevisionAsync(d.TenantId, d.Id, 2, token).ConfigureAwait(false))!.Markdown, "Metadata revision retains body");
                EnumerationResult<PromptDocumentRevisionInfo> metadata = await db.PromptDocuments.ListRevisionMetadataAsync(d.TenantId, d.Id, new EnumerationQuery { MaxResults = 1 }, token).ConfigureAwait(false);
                Assert.Equal(2L, metadata.TotalRecords, "Revision metadata total");
                Assert.Equal(2, metadata.Objects.Single().Revision, "Revision metadata page is newest first");
                EnumerationResult<PromptDocumentRevisionInfo> second = await db.PromptDocuments.ListRevisionMetadataAsync(d.TenantId, d.Id, new EnumerationQuery { MaxResults = 1, Skip = 1 }, token).ConfigureAwait(false);
                Assert.Equal(1, second.Objects.Single().Revision, "Metadata offset pages correctly");
                Assert.True(await db.PromptDocuments.ReadRevisionMetadataAsync("other-tenant", d.Id, 1, token).ConfigureAwait(false) == null, "Foreign revision metadata is invisible");
            }
        }

        private static async Task CasAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await DatabaseAsync(provider, token).ConfigureAwait(false))
            {
                PromptDocument d = await CreateAsync(db, "Concurrent.md", "initial", token).ConfigureAwait(false);
                PromptDocument a = (await db.PromptDocuments.ReadAsync(d.TenantId, d.Id, token).ConfigureAwait(false))!;
                PromptDocument b = (await db.PromptDocuments.ReadAsync(d.TenantId, d.Id, token).ConfigureAwait(false))!;
                bool[] results = await Task.WhenAll(db.PromptDocuments.TryUpdateAsync(a, new PromptDocumentRevision { Markdown = "first" }, 1, token), db.PromptDocuments.TryUpdateAsync(b, new PromptDocumentRevision { Markdown = "second" }, 1, token)).ConfigureAwait(false);
                Assert.Equal(1, results.Count(x => x), "Exactly one CAS winner");
                PromptDocument stored = (await db.PromptDocuments.ReadAsync(d.TenantId, d.Id, token).ConfigureAwait(false))!;
                Assert.Equal(2L, stored.Version, "Only one version allocated");
                Assert.Equal(2, (await db.PromptDocuments.ListRevisionsAsync(d.TenantId, d.Id, token).ConfigureAwait(false)).Count, "Losing write leaves no orphan revision");
                PromptDocumentRevision current = (await db.PromptDocuments.ReadRevisionAsync(d.TenantId, d.Id, stored.CurrentRevision, token).ConfigureAwait(false))!;
                Assert.True(await db.PromptDocuments.TryUpdateAsync(stored, new PromptDocumentRevision { Markdown = current.Markdown }, stored.Version, token).ConfigureAwait(false), "No-op accepted");
                Assert.Equal(2L, stored.Version, "No-op keeps version");
                await RejectAsync<ConflictException>(() => db.PromptDocuments.DeleteAsync(d.TenantId, d.Id, 1, token)).ConfigureAwait(false);
            }
        }

        private static async Task AttachmentsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await DatabaseAsync(provider, token).ConfigureAwait(false))
            {
                AgentProfile p = await ProfileAsync(db, token).ConfigureAwait(false);
                PromptDocument d = await CreateAsync(db, "One.md", "one", token).ConfigureAwait(false);
                PromptDocument e = await CreateAsync(db, "Two.md", "two", token).ConfigureAwait(false);
                AgentPromptDocument a = await db.AgentPromptDocuments.CreateAsync(Attachment(p, d), 0, token).ConfigureAwait(false);
                AgentPromptDocument b = await db.AgentPromptDocuments.CreateAsync(Attachment(p, e), 1, token).ConfigureAwait(false);
                await RejectAsync<ConflictException>(() => db.AgentPromptDocuments.CreateAsync(Attachment(p, d), 2, token)).ConfigureAwait(false);
                await RejectAsync<ConflictException>(() => db.PromptDocuments.DeleteAsync(d.TenantId, d.Id, 1, token)).ConfigureAwait(false);
                await RejectAsync<ConflictException>(() => db.AgentPromptDocuments.ReorderAsync(p.TenantId, p.Id, new List<string> { a.Id }, 2, token)).ConfigureAwait(false);
                List<AgentPromptDocument> list = await db.AgentPromptDocuments.ListAsync(p.TenantId, p.Id, token).ConfigureAwait(false);
                List<string> reversed = list.Select(x => x.Id).Reverse().ToList();
                long version = await db.AgentPromptDocuments.ReorderAsync(p.TenantId, p.Id, reversed, 2, token).ConfigureAwait(false);
                Assert.Equal(3L, version, "Reorder increments set once");
                Assert.True((await db.AgentPromptDocuments.ListAsync(p.TenantId, p.Id, token).ConfigureAwait(false)).Select(x => x.Id).SequenceEqual(reversed), "Order exact");
                await RejectAsync<ConflictException>(() => db.AgentPromptDocuments.DeleteAsync(p.TenantId, p.Id, a.Id, 1, 3, token)).ConfigureAwait(false);
                await db.AgentProfiles.DeleteCascadeAsync(p.TenantId, p.Id, token).ConfigureAwait(false);
                Assert.Equal(0, (await db.AgentPromptDocuments.ReferrersAsync(p.TenantId, d.Id, token).ConfigureAwait(false)).Count, "Profile delete removes attachments");
                Assert.True(await db.PromptDocuments.DeleteAsync(d.TenantId, d.Id, 1, token).ConfigureAwait(false), "Detached document can be deleted");
                Assert.True(await db.PromptDocuments.ReadRevisionAsync(d.TenantId, d.Id, 1, token).ConfigureAwait(false) == null, "Document deletion removes revisions");
                Assert.True(await db.PromptDocuments.ReadAsync(e.TenantId, e.Id, token).ConfigureAwait(false) != null, "Other library document survives profile deletion");
            }
        }

        private static async Task PinCollisionAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await DatabaseAsync(provider, token).ConfigureAwait(false))
            {
                AgentProfile p = await ProfileAsync(db, token).ConfigureAwait(false);
                PromptDocument d = await CreateAsync(db, "Original.md", "old body", token).ConfigureAwait(false);
                AgentPromptDocument pin = Attachment(p, d);
                pin.RevisionPolicy = PromptDocumentRevisionPolicyEnum.Pinned;
                pin.PinnedRevision = 1;
                await db.AgentPromptDocuments.CreateAsync(pin, 0, token).ConfigureAwait(false);
                d.Name = "Renamed.md";
                Assert.True(await db.PromptDocuments.TryUpdateAsync(d, null, 1, token).ConfigureAwait(false), "Rename shared library document");
                PromptDocument e = await CreateAsync(db, "Original.md", "different body", token).ConfigureAwait(false);
                await RejectAsync<ConflictException>(() => db.AgentPromptDocuments.CreateAsync(Attachment(p, e), 1, token)).ConfigureAwait(false);
                Assert.Equal(1L, (await db.AgentProfiles.ReadAsync(p.TenantId, p.Id, token).ConfigureAwait(false))!.DocumentSetVersion, "Collision transaction rolls back set version");
                e.Name = "Another.md";
                await db.PromptDocuments.TryUpdateAsync(e, null, 1, token).ConfigureAwait(false);
                await db.AgentPromptDocuments.CreateAsync(Attachment(p, e), 1, token).ConfigureAwait(false);
                e.Name = "Original.md";
                await RejectAsync<ConflictException>(() => db.PromptDocuments.TryUpdateAsync(e, null, 2, token)).ConfigureAwait(false);
                Assert.Equal(2, (await db.PromptDocuments.ListRevisionsAsync(e.TenantId, e.Id, token).ConfigureAwait(false)).Count, "Conflicting rename leaves no revision");
                Assert.Equal("Original.md", (await db.PromptDocuments.ReadRevisionAsync(d.TenantId, d.Id, 1, token).ConfigureAwait(false))!.Name, "Pinned name snapshot unchanged");
            }
        }

        private static async Task IsolationLimitsAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await DatabaseAsync(provider, token).ConfigureAwait(false))
            {
                AgentProfile p = await ProfileAsync(db, token).ConfigureAwait(false);
                PromptDocument d = await CreateAsync(db, "Limit.md", "small", token).ConfigureAwait(false);
                Assert.True(await db.PromptDocuments.ReadAsync("other-tenant", d.Id, token).ConfigureAwait(false) == null, "Foreign metadata invisible");
                Assert.True(await db.PromptDocuments.ReadRevisionAsync("other-tenant", d.Id, 1, token).ConfigureAwait(false) == null, "Foreign revision invisible");
                await db.Tenants.CreateAsync(new Tenant { Id = "other-tenant", Name = "other" }, token).ConfigureAwait(false);
                AgentPromptDocument foreign = Attachment(p, d); foreign.TenantId = "other-tenant";
                await RejectAsync<NotFoundException>(() => db.AgentPromptDocuments.CreateAsync(foreign, 0, token)).ConfigureAwait(false);
                AgentPromptDocument invalid = Attachment(p, d); invalid.RevisionPolicy = PromptDocumentRevisionPolicyEnum.Pinned; invalid.PinnedRevision = 99;
                await RejectAsync<ConflictException>(() => db.AgentPromptDocuments.CreateAsync(invalid, 0, token)).ConfigureAwait(false);
                db.PromptDocuments.MaxDocumentBytes = 1024;
                await CreateAsync(db, "Exact.md", new string('x', 1024), token).ConfigureAwait(false);
                await RejectAsync<PromptDocumentException>(() => CreateAsync(db, "Oversize.md", new string('x', 1025), token)).ConfigureAwait(false);
                await RejectAsync<ValidationException>(() => CreateAsync(db, "Binary.md", "a\0b", token)).ConfigureAwait(false);
                await RejectAsync<ValidationException>(() => CreateAsync(db, "../Bad.md", "body", token)).ConfigureAwait(false);
                db.AgentPromptDocuments.MaxAttachmentsPerProfile = 1;
                await db.AgentPromptDocuments.CreateAsync(Attachment(p, d), 0, token).ConfigureAwait(false);
                PromptDocument second = await CreateAsync(db, "Second.md", "body", token).ConfigureAwait(false);
                await RejectAsync<PromptDocumentException>(() => db.AgentPromptDocuments.CreateAsync(Attachment(p, second), 1, token)).ConfigureAwait(false);
                Assert.Equal(1L, (await db.AgentProfiles.ReadAsync(p.TenantId, p.Id, token).ConfigureAwait(false))!.DocumentSetVersion, "Quota failure is atomic");
            }
        }

        private static async Task TenantQuotasAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await DatabaseAsync(provider, token).ConfigureAwait(false))
            {
                db.PromptDocuments.MaxDocumentsPerTenant = 1;
                int[] outcomes = await Task.WhenAll(QuotaCreateAsync(db, "First.md", token), QuotaCreateAsync(db, "Second.md", token)).ConfigureAwait(false);
                Assert.Equal(1, outcomes.Count(x => x == 1), "Concurrent creates cannot exceed tenant count");
                PromptDocument d = (await db.PromptDocuments.ListAsync("documents-tenant", token).ConfigureAwait(false)).Single();
                await db.PromptDocuments.DeleteAsync(d.TenantId, d.Id, 1, token).ConfigureAwait(false);
                db.PromptDocuments.MaxDocumentBytes = 1048576;
                db.PromptDocuments.MaxStorageBytesPerTenant = 1048576;
                d = await CreateAsync(db, "Storage.md", new string('x', 1048576), token).ConfigureAwait(false);
                await RejectAsync<PromptDocumentException>(() => db.PromptDocuments.TryUpdateAsync(d, new PromptDocumentRevision { Markdown = "new" }, 1, token)).ConfigureAwait(false);
                Assert.Equal(1, (await db.PromptDocuments.ListRevisionsAsync(d.TenantId, d.Id, token).ConfigureAwait(false)).Count, "History quota refusal leaves no orphan revision");
                Assert.Equal(1L, (await db.PromptDocuments.ReadAsync(d.TenantId, d.Id, token).ConfigureAwait(false))!.Version, "Quota refusal leaves version unchanged");
                await db.PromptDocuments.DeleteAsync(d.TenantId, d.Id, 1, token).ConfigureAwait(false);
                await CreateAsync(db, "Freed.md", new string('y', 1048576), token).ConfigureAwait(false);
            }
        }

        private static async Task<int> QuotaCreateAsync(DatabaseDriverBase db, string name, CancellationToken token)
        {
            try { await CreateAsync(db, name, "small", token).ConfigureAwait(false); return 1; }
            catch (PromptDocumentException) { return 0; }
        }

        private static async Task PublicEditGuardAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await DatabaseAsync(provider, token).ConfigureAwait(false))
            {
                AgentProfile p = await ProfileAsync(db, token).ConfigureAwait(false);
                PromptDocument d = await CreateAsync(db, "Published.md", "approved", token).ConfigureAwait(false);
                AgentPromptDocument a = Attachment(p, d);
                a.AllowPublicAccess = true;
                await db.AgentPromptDocuments.CreateAsync(a, 0, token).ConfigureAwait(false);
                await RejectAsync<ForbiddenException>(() => db.PromptDocuments.TryUpdateAsync(d, new PromptDocumentRevision { Markdown = "unapproved" }, 1, token)).ConfigureAwait(false);
                Assert.Equal(1, (await db.PromptDocuments.ListRevisionsAsync(d.TenantId, d.Id, token).ConfigureAwait(false)).Count, "Denied content is not persisted");
                Assert.True(await db.PromptDocuments.TryUpdateAsync(d, new PromptDocumentRevision { Markdown = "approved replacement" }, 1, true, token).ConfigureAwait(false), "Explicitly authorized document Admin write succeeds");
                d.Active = false;
                Assert.True(await db.PromptDocuments.TryUpdateAsync(d, null, 2, token).ConfigureAwait(false), "Update permission can revoke delivery");
            }
        }

        private static async Task ParentDeleteRaceAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await DatabaseAsync(provider, token).ConfigureAwait(false))
            {
                AgentProfile p = await ProfileAsync(db, token).ConfigureAwait(false);
                PromptDocument d = await CreateAsync(db, "Race.md", "body", token).ConfigureAwait(false);
                await Task.WhenAll((Task)TryAttachDuringDeleteAsync(db, p, d, token), db.AgentProfiles.DeleteCascadeAsync(p.TenantId, p.Id, token)).ConfigureAwait(false);
                Assert.Equal(0, (await db.AgentPromptDocuments.ListAsync(p.TenantId, p.Id, token).ConfigureAwait(false)).Count, "Profile delete leaves no concurrent attachment");
                await Task.WhenAll((Task)TryCreateDuringDeleteAsync(db, token), db.Tenants.DeleteCascadeAsync(p.TenantId, token)).ConfigureAwait(false);
                Assert.Equal(0, (await db.PromptDocuments.ListAsync(p.TenantId, token).ConfigureAwait(false)).Count, "Tenant delete leaves no concurrent document");
            }
        }

        private static async Task TryAttachDuringDeleteAsync(DatabaseDriverBase db, AgentProfile p, PromptDocument d, CancellationToken token)
        {
            try { await db.AgentPromptDocuments.CreateAsync(Attachment(p, d), 0, token).ConfigureAwait(false); }
            catch (NotFoundException) { }
            catch (ConflictException) { }
        }

        private static async Task TryCreateDuringDeleteAsync(DatabaseDriverBase db, CancellationToken token)
        {
            try { await CreateAsync(db, "Concurrent tenant deletion.md", "body", token).ConfigureAwait(false); }
            catch (NotFoundException) { }
        }

        private static async Task VersionExhaustionAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await DatabaseAsync(provider, token).ConfigureAwait(false))
            {
                long maximum = 9007199254740991L;
                PromptDocument d = await CreateAsync(db, "Exhausted.md", "body", token).ConfigureAwait(false);
                AgentProfile p = await ProfileAsync(db, token).ConfigureAwait(false);
                AdoDatabaseDriver ado = (AdoDatabaseDriver)db;
                await ado.NonQueryAsync("UPDATE " + ado.Table("prompt_documents") + " SET metadata_version=@maximum WHERE tenant_id=@tenant AND id=@id;", command => { AdoDatabaseDriver.Add(command, "maximum", maximum); AdoDatabaseDriver.Add(command, "tenant", d.TenantId); AdoDatabaseDriver.Add(command, "id", d.Id); }, token).ConfigureAwait(false);
                Assert.True(await db.PromptDocuments.TryUpdateAsync(d, null, maximum, token).ConfigureAwait(false), "Exhausted document accepts an unchanged save");
                Assert.Equal(maximum, d.Version, "No-op retains maximum safe counter");
                try
                {
                    await db.PromptDocuments.TryUpdateAsync(d, new PromptDocumentRevision { Markdown = "changed" }, maximum, token).ConfigureAwait(false);
                    throw new TestAssertionException("Expected a controlled exhaustion conflict.");
                }
                catch (ConflictException error) { Assert.Equal("version_exhausted", error.Code, "Stable exhaustion code"); }
                Assert.Equal(maximum, (await db.PromptDocuments.ReadAsync(d.TenantId, d.Id, token).ConfigureAwait(false))!.Version, "Exhaustion cannot wrap document version");
                Assert.Equal(1, (await db.PromptDocuments.ListRevisionsAsync(d.TenantId, d.Id, token).ConfigureAwait(false)).Count, "Exhaustion leaves no orphan revision");
                await ado.NonQueryAsync("UPDATE " + ado.Table("agent_profiles") + " SET document_set_version=@maximum WHERE tenant_id=@tenant AND id=@id;", command => { AdoDatabaseDriver.Add(command, "maximum", maximum); AdoDatabaseDriver.Add(command, "tenant", p.TenantId); AdoDatabaseDriver.Add(command, "id", p.Id); }, token).ConfigureAwait(false);
                await RejectAsync<ConflictException>(() => db.AgentPromptDocuments.CreateAsync(Attachment(p, d), maximum, token)).ConfigureAwait(false);
                Assert.Equal(0, (await db.AgentPromptDocuments.ListAsync(p.TenantId, p.Id, token).ConfigureAwait(false)).Count, "Set exhaustion cannot insert an attachment");
                Assert.Equal(maximum, (await db.AgentProfiles.ReadAsync(p.TenantId, p.Id, token).ConfigureAwait(false))!.DocumentSetVersion, "Set exhaustion cannot wrap profile version");
            }
        }

        private static async Task RevocationAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            using (DatabaseDriverBase db = await DatabaseAsync(provider, token).ConfigureAwait(false))
            {
                AgentProfile p = await ProfileAsync(db, token).ConfigureAwait(false);
                PromptDocument d = await CreateAsync(db, "Revoke.md", "body", token).ConfigureAwait(false);
                AgentPromptDocument a = await db.AgentPromptDocuments.CreateAsync(Attachment(p, d), 0, token).ConfigureAwait(false);
                AgentProfile fresh = (await db.AgentProfiles.ReadAsync(p.TenantId, p.Id, token).ConfigureAwait(false))!;
                Assert.Equal(p.ChatRevision + 1, fresh.ChatRevision, "Attach revokes old visitor capability");
                p.Description = "Unrelated profile edit";
                await db.AgentProfiles.UpdateAsync(p, false, false, token).ConfigureAwait(false);
                Assert.Equal(1L, (await db.AgentProfiles.ReadAsync(p.TenantId, p.Id, token).ConfigureAwait(false))!.DocumentSetVersion, "Stale profile update preserves attachment set");
                d.Active = false;
                await db.PromptDocuments.TryUpdateAsync(d, null, 1, token).ConfigureAwait(false);
                Assert.Equal(fresh.ChatRevision + 1, (await db.AgentProfiles.ReadAsync(p.TenantId, p.Id, token).ConfigureAwait(false))!.ChatRevision, "Document deactivation revokes referrers");
                a.Enabled = false;
                await db.AgentPromptDocuments.UpdateAsync(a, 1, 1, token).ConfigureAwait(false);
                Assert.Equal(2L, (await db.AgentProfiles.ReadAsync(p.TenantId, p.Id, token).ConfigureAwait(false))!.DocumentSetVersion, "Attachment disable updates set");
                await db.AgentPromptDocuments.UpdateAsync(a, 2, 2, token).ConfigureAwait(false);
                Assert.Equal(2L, (await db.AgentProfiles.ReadAsync(p.TenantId, p.Id, token).ConfigureAwait(false))!.DocumentSetVersion, "No-op attachment keeps set");
            }
        }

        private static async Task<DatabaseDriverBase> DatabaseAsync(DatabaseTypeEnum provider, CancellationToken token)
        {
            DatabaseDriverBase db = await TestEnvironment.CreateDriverAsync(provider, token).ConfigureAwait(false);
            await db.Tenants.CreateAsync(new Tenant { Id = "documents-tenant", Name = "Documents test" }, token).ConfigureAwait(false);
            Assert.Equal(SchemaBuilder.LatestVersion, db.SchemaVersion, "Migrated schema version");
            await db.InitializeAsync(token).ConfigureAwait(false);
            return db;
        }

        private static Task<AgentProfile> ProfileAsync(DatabaseDriverBase db, CancellationToken token) { return db.AgentProfiles.CreateAsync(new AgentProfile { TenantId = "documents-tenant", Name = "documents-agent" }, token); }
        private static Task<PromptDocument> CreateAsync(DatabaseDriverBase db, string name, string markdown, CancellationToken token) { return db.PromptDocuments.CreateAsync(new PromptDocument { TenantId = "documents-tenant", Name = name, Description = "Read the schema before use." }, new PromptDocumentRevision { Markdown = markdown }, token); }
        private static AgentPromptDocument Attachment(AgentProfile p, PromptDocument d) { return new AgentPromptDocument { TenantId = p.TenantId, AgentProfileId = p.Id, DocumentId = d.Id }; }
        private static async Task RejectAsync<T>(Func<Task> action) where T : Exception
        {
            try { await action().ConfigureAwait(false); }
            catch (T) { return; }
            throw new TestAssertionException("Expected " + typeof(T).Name + ".");
        }
    }
}

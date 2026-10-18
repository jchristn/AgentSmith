import { describe, expect, it } from "vitest";
import { owner, unique, expectHugoError } from "./helpers.js";

describe("agent prompt documents", () => {
  it("versions content, scopes attachments, switches delivery, pins, reorders and cleans up", async () => {
    const c = owner();
    const profile = await c.agentProfiles.create({ name: unique("document-agent") });
    const document = await c.promptDocuments.create({ name: `${unique("LOG_STRUCTURE")}.md`, description: "Read before interpreting logs.", markdown: "# Synthetic log fields\nThe field event_code identifies an event.\n", visibility: "Tenant" });
    try {
      expect((await c.promptDocuments.revision(document.id, 1)).markdown).toContain("event_code");
      const set = await c.agentPromptDocuments.list(profile.id);
      const attachment = await c.agentPromptDocuments.create(profile.id, { expectedDocumentSetVersion: set.documentSetVersion, documentId: document.id, deliveryMode: "Reference", revisionPolicy: "Latest", enabled: true });
      const reference = await c.agentPromptDocuments.preview(profile.id);
      expect(reference.compiledText).toContain(document.name);
      expect(reference.compiledText).not.toContain("The field event_code identifies an event.");
      const inline = await c.agentPromptDocuments.update(profile.id, attachment.id, { expectedVersion: attachment.version, expectedDocumentSetVersion: attachment.documentSetVersion, deliveryMode: "Inline", revisionPolicy: "Pinned", pinnedRevision: 1 });
      expect((await c.agentPromptDocuments.preview(profile.id)).compiledText).toContain("The field event_code identifies an event.");
      await expectHugoError(c.promptDocuments.update(document.id, { expectedVersion: 999999, markdown: "stale" }), 409);
      const revised = await c.promptDocuments.update(document.id, { expectedVersion: document.version, markdown: "# New revision\nChanged fields.\n" });
      expect(revised.currentRevision).toBe(2);
      expect((await c.agentPromptDocuments.preview(profile.id)).compiledText).not.toContain("Changed fields.");
      await expectHugoError(c.promptDocuments.delete(document.id, revised.version), 409);
      const reordered = await c.agentPromptDocuments.reorder(profile.id, { expectedDocumentSetVersion: inline.documentSetVersion, attachmentIds: [attachment.id] });
      const refreshed = await c.agentPromptDocuments.list(profile.id);
      await c.agentPromptDocuments.delete(profile.id, attachment.id, refreshed.items[0].version, refreshed.documentSetVersion);
      expect((await c.promptDocuments.referrers(document.id)).items).toHaveLength(0);
    } finally {
      await c.agentProfiles.delete(profile.id);
      const current = await c.promptDocuments.get(document.id);
      await c.promptDocuments.delete(document.id, current.version);
    }
  });
});

import { test, expect } from '@playwright/test';
import { installMocks, login } from './mocks.js';

async function documents(page) {
  await installMocks(page);
  let doc = {
    id: 'pdc_schema',
    name: 'LOG_MESSAGE_STRUCTURE.md',
    description: 'Read before analyzing application logs',
    visibility: 'Tenant',
    active: true,
    version: 1,
    currentRevision: 1,
    contentBytes: 55,
    ownerUserId: 'usr_admin',
    canUpdate: true,
    canDelete: true,
    hasReferrers: false
  };
  let markdown = '# Log schema\n\n| Field | Meaning |\n| --- | --- |\n| severity | Level |\n\n{{literal}}';
  let attachments = [];
  let setVersion = 1;
  const calls = [];
  await page.route('**/v1.0/api/prompt-documents**', async (route) => {
    const url = new URL(route.request().url());
    const method = route.request().method();
    if (method === 'POST') {
      calls.push({ ...route.request().postDataJSON(), requestMethod: method });
      return route.fulfill({ status: 201, json: doc });
    }
    if (method === 'PUT') {
      const body = route.request().postDataJSON();
      calls.push(body);
      doc = { ...doc, ...body, version: doc.version + 1 };
      markdown = body.markdown;
      return route.fulfill({ json: doc });
    }
    if (url.pathname.endsWith('/referrers'))
      return route.fulfill({ json: { items: attachments.map((r) => ({ ...r, profileId: 'prf_0001', profileName: 'Hugo' })), hasOtherReferrers: false } });
    if (/\/revisions\/\d+$/.test(url.pathname)) return route.fulfill({ json: { ...doc, revision: 1, markdown } });
    if (url.pathname.endsWith('/revisions')) return route.fulfill({ json: { objects: [{ revision: 1 }], totalRecords: 1 } });
    if (url.pathname.endsWith('/pdc_schema')) return route.fulfill({ json: doc });
    return route.fulfill({ json: { objects: [doc], totalRecords: 1, maxResults: 25, skip: 0 } });
  });
  await page.route('**/v1.0/api/agent-profiles/*/documents**', async (route) => {
    const url = new URL(route.request().url());
    const method = route.request().method();
    if (url.pathname.endsWith('/preview'))
      return route.fulfill({
        json: {
          compiledText: 'Attached reference documents: LOG_MESSAGE_STRUCTURE.md',
          manifest: attachments,
          segments: [],
          issues: [],
          inlineBytes: 0,
          manifestBytes: 128,
          estimatedTokens: 32
        }
      });
    if (method === 'POST') {
      const body = route.request().postDataJSON();
      calls.push(body);
      attachments = [
        { ...body, id: 'pda_schema', name: doc.name, description: doc.description, version: 1, revision: 1, resolvedRevision: 1, contentBytes: 55 }
      ];
      setVersion++;
      return route.fulfill({ json: attachments[0] });
    }
    if (method === 'PUT') {
      calls.push(route.request().postDataJSON());
      attachments = attachments.map((r) => ({ ...r, ...route.request().postDataJSON(), version: r.version + 1 }));
      setVersion++;
      return route.fulfill({ json: attachments[0] });
    }
    if (method === 'DELETE') {
      attachments = [];
      setVersion++;
      return route.fulfill({ status: 204 });
    }
    return route.fulfill({ json: { documentSetVersion: setVersion, items: attachments } });
  });
  return calls;
}

test('imports, attaches, pins, previews and detaches a managed document', async ({ page }) => {
  const calls = await documents(page);
  await login(page, { expect });
  await page.goto('/dashboard/instructions?tab=documents');
  await page.getByRole('button', { name: 'Add document', exact: true }).click();
  await expect(page.getByTestId('document-editor').locator('.modal-title')).toHaveText('Add document');
  await page.locator('input[type=file]').setInputFiles({ name: 'example.md', mimeType: 'text/markdown', buffer: Buffer.from('# Imported\n{{literal}}') });
  await expect(page.locator('#document-name')).toHaveValue('example.md');
  await page.locator('#document-description').fill('Read before interpreting examples');
  await expect(page.locator('.markdown-body')).toContainText('{{literal}}');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByTestId('document-editor')).toHaveCount(0);
  expect(calls[0]).toMatchObject({ requestMethod: 'POST', name: 'example.md', markdown: '# Imported\n{{literal}}', visibility: 'Private' });
  await page.goto('/dashboard/profiles/prf_0001?tab=instructions');
  await page.getByRole('button', { name: 'Attach document', exact: true }).click();
  await page.locator('#attachment-documentId').selectOption('pdc_schema');
  await expect(page.locator('#attachment-deliveryMode')).toHaveValue('Reference');
  await page.locator('#attachment-revisionPolicy').selectOption('Pinned');
  await page.locator('#attachment-pinnedRevision').selectOption('1');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(page.getByTestId('attachment-editor')).toHaveCount(0);
  expect(calls.at(-1)).toMatchObject({ documentId: 'pdc_schema', expectedDocumentSetVersion: 1, revisionPolicy: 'Pinned', pinnedRevision: 1 });
  await page.getByTestId('profile-documents').getByRole('button', { name: 'Preview', exact: true }).click();
  await expect(page.getByTestId('profile-documents')).toContainText('Attached reference documents:');
  await page.getByRole('button', { name: 'Detach', exact: true }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Confirm', exact: true }).click();
  await expect(page.getByTestId('profile-documents')).toContainText('No documents attached');
});

for (const width of [390, 768, 1280])
  for (const theme of ['light', 'dark']) {
    test(`document editor remains usable at ${width}px ${theme}`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 900 });
      await documents(page);
      await login(page, { expect });
      await page.evaluate((theme) => {
        localStorage.setItem('hugo_theme', theme);
      }, theme);
      await page.goto('/dashboard/instructions?tab=documents');
      await page.getByRole('cell', { name: 'LOG_MESSAGE_STRUCTURE.md', exact: true }).click();
      await expect(page.locator('#document-markdown')).toHaveValue(/Log schema/);
      await expect(page.getByRole('heading', { name: 'Log schema' })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
      await page.getByRole('button', { name: 'Save', exact: true }).scrollIntoViewIfNeeded();
      await expect(page.getByRole('button', { name: 'Save', exact: true })).toBeInViewport();
      await page.screenshot({ path: testInfo.outputPath(`documents-${width}-${theme}.png`) });
      await page.keyboard.press('Escape');
      await expect(page.getByTestId('document-editor')).toHaveCount(0);
    });
  }

for (const [locale, label] of [
  ['es', 'Nombre de archivo'],
  ['ja', 'ファイル名'],
  ['ar', 'اسم الملف'],
  ['en-XA', null]
]) {
  test(`document workflows support ${locale} and accessible keyboard focus`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 900 });
    await documents(page);
    await login(page, { expect });
    await page.evaluate((locale) => localStorage.setItem('hugo.locale', locale), locale);
    await page.goto('/dashboard/instructions?tab=documents');
    await page.getByRole('cell', { name: 'LOG_MESSAGE_STRUCTURE.md', exact: true }).click();
    await expect(page.locator('#document-markdown')).toHaveValue(/Log schema/);
    if (label) await expect(page.locator('label[for=document-name]')).toContainText(label);
    if (locale === 'ar') await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.keyboard.press('Tab');
    expect(await page.evaluate(() => !!document.activeElement.closest('[role=dialog]'))).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('document-editor')).toHaveCount(0);
  });
}

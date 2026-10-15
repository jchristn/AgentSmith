// Minimal line diff (longest common subsequence). Adequate for prompt templates and manifests.

/**
 * @param {string} a Before text.
 * @param {string} b After text.
 * @returns {Array<{type:'same'|'add'|'del', text:string}>}
 */
export function diffLines(a, b) {
  const x = String(a).split('\n');
  const y = String(b).split('\n');
  const n = x.length;
  const m = y.length;
  if (n * m > 4_000_000) {
    return [...x.map((text) => ({ type: 'del', text })), ...y.map((text) => ({ type: 'add', text }))];
  }
  const lcs = Array.from({ length: n + 1 }, () => new Int32Array(m + 1));
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      lcs[i][j] = x[i] === y[j] ? lcs[i + 1][j + 1] + 1 : Math.max(lcs[i + 1][j], lcs[i][j + 1]);
    }
  }
  const out = [];
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (x[i] === y[j]) {
      out.push({ type: 'same', text: x[i] });
      i++;
      j++;
    } else if (lcs[i + 1][j] >= lcs[i][j + 1]) {
      out.push({ type: 'del', text: x[i++] });
    } else {
      out.push({ type: 'add', text: y[j++] });
    }
  }
  while (i < n) out.push({ type: 'del', text: x[i++] });
  while (j < m) out.push({ type: 'add', text: y[j++] });
  return out;
}

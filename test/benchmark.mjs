import fs from 'node:fs';
import zlib from 'node:zlib';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const dbPath = path.resolve(__dirname, '../data/en2zhs.json.gz');

console.log('=== MTGO CN Overlay: Core Engine Benchmark ===\n');

// 1. Benchmark Database Loading
console.time('Load & Decompress DB');
const compressedBuf = fs.readFileSync(dbPath);
const jsonString = zlib.gunzipSync(compressedBuf).toString('utf-8');
const db = JSON.parse(jsonString);
console.timeEnd('Load & Decompress DB');

const cards = db.cards || {};
const cardNames = Object.keys(cards);
console.log(`Cards loaded: ${cardNames.length}`);
console.log(`Memory footprint of JSON raw: ${(jsonString.length / 1024 / 1024).toFixed(2)} MB\n`);

// 2. Build normalized lookup map
console.time('Build Normalized Index');
const normMap = new Map();
function normalize(s) {
  return s.toLowerCase().replace(/[^a-z0-9]/g, '');
}

for (const name of cardNames) {
  normMap.set(normalize(name), name);
}
console.timeEnd('Build Normalized Index');
console.log(`Indexed keys: ${normMap.size}\n`);

// 3. Exact Match Benchmark
console.time('1000 Exact Lookups');
for (let i = 0; i < 1000; i++) {
  const target = normalize('Lightning Bolt');
  const matched = normMap.get(target);
  const card = cards[matched];
}
console.timeEnd('1000 Exact Lookups');

// 4. Fuzzy Match (Levenshtein with length bound)
function boundedLevenshtein(a, b, maxLimit) {
  const la = a.length, lb = b.length;
  if (Math.abs(la - lb) >= maxLimit) return maxLimit;
  const d = [];
  for (let i = 0; i <= la; i++) d[i] = [i];
  for (let j = 0; j <= lb; j++) d[0][j] = j;
  for (let i = 1; i <= la; i++) {
    let minRow = d[i][0];
    for (let j = 1; j <= lb; j++) {
      const cost = a[i - 1] === b[j - 1] ? 0 : 1;
      d[i][j] = Math.min(d[i - 1][j] + 1, d[i][j - 1] + 1, d[i - 1][j - 1] + cost);
      if (d[i][j] < minRow) minRow = d[i][j];
    }
    if (minRow >= maxLimit) return maxLimit;
  }
  return d[la][lb];
}

function fuzzyLookup(input, maxDist = 3) {
  const norm = normalize(input);
  if (normMap.has(norm)) {
    return { name: normMap.get(norm), card: cards[normMap.get(norm)], distance: 0 };
  }

  let best = null, bestDist = maxDist + 1;
  for (const [candNorm, original] of normMap.entries()) {
    const d = boundedLevenshtein(norm, candNorm, bestDist);
    if (d < bestDist) {
      bestDist = d;
      best = original;
      if (d <= 1) break;
    }
  }

  if (bestDist <= maxDist) {
    return { name: best, card: cards[best], distance: bestDist };
  }
  return null;
}

// 5. Test OCR Corrupted Strings
const testCases = [
  { rawOcr: 'Lightning Bolt', note: 'Clean Exact' },
  { rawOcr: 'L1ghtn1ng B0lt', note: 'OCR Noise (1->i, 0->o)' },
  { rawOcr: 'Tarm0g0yf', note: 'Single character noise' },
  { rawOcr: 'Liliana of the Veyl', note: 'Spelling error (Veil->Veyl)' },
  { rawOcr: 'Murktide Regent', note: 'Clean Modern staple' },
  { rawOcr: 'Muktide Regnt', note: 'Missing letters' }
];

console.log('Testing OCR Noise Correction:');
for (const tc of testCases) {
  console.time(`Match: "${tc.rawOcr}"`);
  const result = fuzzyLookup(tc.rawOcr, 3);
  console.timeEnd(`Match: "${tc.rawOcr}"`);
  if (result) {
    console.log(`  -> Matched: "${result.name}" (Dist: ${result.distance}, 中文名: ${result.card.n})`);
  } else {
    console.log(`  -> Failed to match`);
  }
}

console.log('\n=== All Benchmark Tests Completed Successfully! ===');

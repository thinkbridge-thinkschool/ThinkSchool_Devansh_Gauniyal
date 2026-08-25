#!/usr/bin/env node
// Structural checks over the source tree. Plain Node, run separately from
// `ng test`: the Vitest-based unit-test builder compiles spec files through
// the same browser-targeted esbuild/Angular-compiler pipeline as `ng build`,
// which has no Node built-ins (this was learned the hard way in Day 13 Task 1
// — see that task's verification-log.md). Static source-text checks belong
// here instead.
import { execFileSync } from 'node:child_process';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const projectRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const srcDir = join(projectRoot, 'src');

function listFiles(dir, extensions) {
  const results = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      results.push(...listFiles(full, extensions));
    } else if (extensions.some((ext) => entry.endsWith(ext))) {
      results.push(full);
    }
  }
  return results;
}

const failures = [];

function check(name, condition, detail) {
  if (condition) {
    console.log(`PASS: ${name}`);
  } else {
    console.log(`FAIL: ${name}`);
    if (detail) console.log(`      ${detail}`);
    failures.push(name);
  }
}

const allSourceFiles = listFiles(srcDir, ['.ts', '.html']);
const nonSpecTsFiles = listFiles(srcDir, ['.ts']).filter((f) => !f.endsWith('.spec.ts'));

const ngModuleOffenders = allSourceFiles.filter((f) => readFileSync(f, 'utf-8').includes('NgModule'));
check('no NgModule anywhere in app source', ngModuleOffenders.length === 0, ngModuleOffenders.join(', '));

const constructorOffenders = nonSpecTsFiles.filter((f) => readFileSync(f, 'utf-8').includes('constructor('));
check(
  'no constructor-parameter injection in non-spec source files',
  constructorOffenders.length === 0,
  constructorOffenders.join(', '),
);

const ANY_PATTERN = /:\s*any\b|<\s*any\s*>|\bas\s+any\b|\bany\[\]/;
const anyOffenders = [];
for (const f of allSourceFiles.filter((f) => f.endsWith('.ts'))) {
  const content = readFileSync(f, 'utf-8');
  for (const line of content.split('\n')) {
    if (ANY_PATTERN.test(line)) {
      anyOffenders.push(`${f}: ${line.trim()}`);
    }
  }
}
check('no `any` anywhere (`: any`, `as any`, `<any>`, `any[]`)', anyOffenders.length === 0, anyOffenders.join(' | '));

const tsconfigAppShow = JSON.parse(
  execFileSync('npx', ['tsc', '--showConfig', '-p', join(projectRoot, 'tsconfig.app.json')], {
    cwd: projectRoot,
  }).toString(),
);
check(
  'tsconfig has strict and noImplicitAny enabled',
  tsconfigAppShow.compilerOptions?.strict === true && tsconfigAppShow.compilerOptions?.noImplicitAny === true,
  JSON.stringify({
    strict: tsconfigAppShow.compilerOptions?.strict,
    noImplicitAny: tsconfigAppShow.compilerOptions?.noImplicitAny,
  }),
);

const quoteBrowserHtml = readFileSync(
  join(srcDir, 'app/quotes/quote-browser/quote-browser.html'),
  'utf-8',
);
const forBlocks = quoteBrowserHtml.match(/@for\s*\([^{]*\)\s*\{/g) ?? [];
const forBlocksWithoutRealTrack = forBlocks.filter((block) => !/track\s+quote\.id/.test(block));
check(
  'every @for block tracks by the real identifier field (quote.id)',
  forBlocks.length > 0 && forBlocksWithoutRealTrack.length === 0,
  forBlocksWithoutRealTrack.join(' | '),
);

const packageJson = readFileSync(join(projectRoot, 'package.json'), 'utf-8');
check('no Zone.js reference in package.json', !packageJson.toLowerCase().includes('zone.js'));

const angularJson = readFileSync(join(projectRoot, 'angular.json'), 'utf-8');
check('no Zone.js reference in angular.json', !angularJson.toLowerCase().includes('zone.js'));

// Day 14 addition: static complement to the DOM-level aria-describedby test in
// create-quote-form.spec.ts -- every id an aria-describedby can reference must
// exist as a literal `id="..."` somewhere in the same template, so the
// binding can never target an id that was simply never written.
const formHtml = readFileSync(
  join(srcDir, 'app/quotes/create-quote-form/create-quote-form.html'),
  'utf-8',
);
const describedByIds = [...formHtml.matchAll(/aria-describedby\]="[^"]*'([\w-]+)'/g)].map((m) => m[1]);
const declaredIds = [...formHtml.matchAll(/\bid="([\w-]+)"/g)].map((m) => m[1]);
const danglingReferences = describedByIds.filter((id) => !declaredIds.includes(id));
check(
  'every aria-describedby target in create-quote-form.html has a matching id in the same template',
  describedByIds.length > 0 && danglingReferences.length === 0,
  danglingReferences.join(', '),
);

// Day 14 Task 2 addition: same aria-describedby-resolvability check, applied
// to the new Signal Forms template.
const signalsFormHtml = readFileSync(
  join(srcDir, 'app/quotes/create-quote-form-signals/create-quote-form-signals.html'),
  'utf-8',
);
const signalsDescribedByIds = [...signalsFormHtml.matchAll(/aria-describedby\]="[^"]*'([\w-]+)'/g)].map(
  (m) => m[1],
);
const signalsDeclaredIds = [...signalsFormHtml.matchAll(/\bid="([\w-]+)"/g)].map((m) => m[1]);
const signalsDanglingReferences = signalsDescribedByIds.filter((id) => !signalsDeclaredIds.includes(id));
check(
  'every aria-describedby target in create-quote-form-signals.html has a matching id in the same template',
  signalsDescribedByIds.length > 0 && signalsDanglingReferences.length === 0,
  signalsDanglingReferences.join(', '),
);

// Confirms the Signal Forms component actually imports from the real path
// verified by reading node_modules/@angular/forms/package.json's "exports"
// map (the Signal Forms Availability Gate), not a guessed or hand-rolled path.
const signalsFormTs = readFileSync(
  join(srcDir, 'app/quotes/create-quote-form-signals/create-quote-form-signals.ts'),
  'utf-8',
);
check(
  "Signal Forms component imports from the real '@angular/forms/signals' path",
  /from ['"]@angular\/forms\/signals['"]/.test(signalsFormTs),
);

if (failures.length > 0) {
  console.error(`\n${failures.length} structural check(s) failed.`);
  process.exit(1);
}
console.log('\nAll structural checks passed.');

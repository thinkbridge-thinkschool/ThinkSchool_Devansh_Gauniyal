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

// Day 15 Task 1 narrowing: the requirement is "no component or interceptor uses
// constructor parameter injection" -- not "no class anywhere may have a constructor".
// A blanket `constructor(` scan over every non-spec file false-positives on
// AppHttpError (src/app/http/app-http-error.ts), a plain `extends Error` data class
// whose constructor sets its own fields and has nothing to do with Angular DI. Scoped
// instead to files that are actually a component (`@Component(`), an interceptor
// (`: HttpInterceptorFn`), a guard (`: CanActivateFn`, Day 16 Task 1) or -- Day 16
// Task 2 addition -- a service (`@Injectable(`, covering QuotesStore), which is
// exactly what this task's requirement ("no component or service") names.
const constructorOffenders = nonSpecTsFiles.filter((f) => {
  const content = readFileSync(f, 'utf-8');
  const isComponentInterceptorGuardOrService =
    content.includes('@Component(') ||
    content.includes(': HttpInterceptorFn') ||
    content.includes(': CanActivateFn') ||
    content.includes('@Injectable(');
  return isComponentInterceptorGuardOrService && content.includes('constructor(');
});
check(
  'no component, interceptor, guard or service uses constructor-parameter injection',
  constructorOffenders.length === 0,
  constructorOffenders.join(', '),
);

// Day 16 Task 1 addition: the lazy-loaded quote-detail-page.ts must never be statically
// imported anywhere outside its own directory -- a stray eager import (even a type-only
// one written without the `type` keyword) would pull it back into the main bundle
// despite app.routes.ts's loadComponent(), silently defeating the lazy split. This is
// the static-source-text complement to the real build-output grep in
// output/lazy-load-proof.md, which is the actual proof; this check exists so a future
// regression is caught by `npm test`-adjacent tooling too, not only by re-reading a
// build log.
const detailPageDir = join(srcDir, 'app/quotes/quote-detail-page');
const eagerDetailImportOffenders = nonSpecTsFiles
  .filter((f) => !f.startsWith(detailPageDir))
  .filter((f) => /from\s+['"][^'"]*quote-detail-page['"]/.test(readFileSync(f, 'utf-8')));
check(
  'quote-detail-page is never statically imported outside its own directory',
  eagerDetailImportOffenders.length === 0,
  eagerDetailImportOffenders.join(', '),
);

// Day 16 Task 1 addition: the detail routes must use loadComponent (lazy), never a
// statically-imported `component` property.
const routesTs = readFileSync(join(srcDir, 'app/app.routes.ts'), 'utf-8');
check(
  'app.routes.ts uses loadComponent for the detail routes, not component',
  routesTs.includes('loadComponent') && !/\bcomponent:\s*\w/.test(routesTs),
);

// Day 16 Task 2 addition: no state-management package of any kind. "Signals first" per
// the brief -- NgRx, @ngrx/signals, or any similar library appearing in package.json
// would mean the task was misread.
const STATE_LIB_PATTERN = /"@ngrx\/|"ngrx|"@ngxs\/|"mobx|"redux|"zustand|"akita|"elf-/i;
const packageJsonForStateLibCheck = readFileSync(join(projectRoot, 'package.json'), 'utf-8');
check(
  'no state-management package appears in package.json',
  !STATE_LIB_PATTERN.test(packageJsonForStateLibCheck),
);

// Day 16 Task 2 addition: QuoteBrowser and QuoteDetailPage must no longer call the API
// themselves -- QuotesStore owns those calls now (see quotes-store.ts). Checked by
// confirming neither file imports HttpClient directly (never did) NOR QuoteApi (did,
// before this task's move) any more.
const quoteBrowserTs = readFileSync(join(srcDir, 'app/quotes/quote-browser/quote-browser.ts'), 'utf-8');
const quoteDetailPageTs = readFileSync(
  join(srcDir, 'app/quotes/quote-detail-page/quote-detail-page.ts'),
  'utf-8',
);
const apiCallOffenders = [];
for (const [label, content] of [
  ['quote-browser.ts', quoteBrowserTs],
  ['quote-detail-page.ts', quoteDetailPageTs],
]) {
  if (/HttpClient|from ['"]\.\.\/quote-api['"]/.test(content)) {
    apiCallOffenders.push(label);
  }
}
check(
  'QuoteBrowser and QuoteDetailPage no longer call the API directly -- QuotesStore owns it',
  apiCallOffenders.length === 0,
  apiCallOffenders.join(', '),
);

// Day 16 Task 2 addition: quoteCount and isEmpty must be `computed(...)` declarations
// and never separately assigned (e.g. `this.quoteCount.set(...)` or a plain `signal()`
// backing them) anywhere in the store -- interpretation 5, "one source of truth".
const storeTs = readFileSync(join(srcDir, 'app/quotes/quotes-store.ts'), 'utf-8');
const derivedOffenders = [];
for (const name of ['quoteCount', 'isEmpty']) {
  const declaredAsComputed = new RegExp(`readonly ${name} = computed\\(`).test(storeTs);
  const separatelyAssigned = new RegExp(`this\\.${name}\\.set\\(|this\\._${name}\\b|${name} = signal\\(`).test(
    storeTs,
  );
  if (!declaredAsComputed || separatelyAssigned) {
    derivedOffenders.push(name);
  }
}
check(
  'quoteCount and isEmpty are computed over quotes(), never separately assigned',
  derivedOffenders.length === 0,
  derivedOffenders.join(', '),
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

// Day 15 Task 1 addition: no real JWT (three base64url segments, the standard
// `eyJ...` header prefix) may appear anywhere in source, committed fixtures, or
// captured live-capture output. Deliberately does not flag the obviously-fake
// 'test-token' literal used throughout the interceptor specs, or the template
// literal `Bearer ${token}` -- both are not JWT-shaped.
const JWT_PATTERN = /eyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}/;
const jwtOffenders = [];
const outputDir = join(projectRoot, 'output');
const filesToScanForTokens = [
  ...allSourceFiles,
  ...listFiles(outputDir, ['.json', '.txt', '.md']),
];
for (const f of filesToScanForTokens) {
  if (JWT_PATTERN.test(readFileSync(f, 'utf-8'))) {
    jwtOffenders.push(f);
  }
}
check('no JWT-shaped bearer token anywhere in source or captured output', jwtOffenders.length === 0, jwtOffenders.join(', '));

if (failures.length > 0) {
  console.error(`\n${failures.length} structural check(s) failed.`);
  process.exit(1);
}
console.log('\nAll structural checks passed.');

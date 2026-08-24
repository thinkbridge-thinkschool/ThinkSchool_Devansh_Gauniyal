#!/usr/bin/env node
// Structural checks over the source tree. Plain Node, run separately from
// `ng test`: the Vitest-based unit-test builder compiles spec files through
// the same browser-targeted esbuild/Angular-compiler pipeline as `ng build`,
// which has no Node built-ins (see verification-log.md for the real error
// this was moved out to fix). Static source-text checks belong here instead.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
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

const quoteListHtml = readFileSync(join(srcDir, 'app/quotes/quote-list/quote-list.html'), 'utf-8');
const forBlocks = quoteListHtml.match(/@for\s*\([^{]*\)\s*\{/g) ?? [];
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

if (failures.length > 0) {
  console.error(`\n${failures.length} structural check(s) failed.`);
  process.exit(1);
}
console.log('\nAll structural checks passed.');

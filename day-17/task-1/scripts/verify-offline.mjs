import { readFile, readdir } from 'node:fs/promises';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const taskRoot = dirname(dirname(fileURLToPath(import.meta.url)));
const ignoredDirectories = new Set([
  '.angular',
  'bin',
  'coverage',
  'dist',
  'node_modules',
  'obj',
]);

const failures = [];

function check(condition, message) {
  if (!condition) {
    failures.push(message);
  }
}

const swaConfigPath = join(taskRoot, 'web/public/staticwebapp.config.json');
const swaConfig = JSON.parse(await readFile(swaConfigPath, 'utf8'));
check(
  swaConfig.navigationFallback?.rewrite === '/index.html',
  'SPA fallback must rewrite navigation requests to /index.html.',
);
check(
  Array.isArray(swaConfig.navigationFallback?.exclude),
  'SPA fallback must exclude static assets from the rewrite.',
);

const apiBaseSource = await readFile(join(taskRoot, 'web/src/app/api-base-url.ts'), 'utf8');
const appConfigSource = await readFile(join(taskRoot, 'web/src/app/app.config.ts'), 'utf8');
check(
  apiBaseSource.includes("new InjectionToken<string>('API_BASE_URL'") &&
    apiBaseSource.includes("factory: () => ''"),
  'The API base URL must remain injectable and default to relative behavior.',
);
check(
  appConfigSource.includes('{ provide: API_BASE_URL, useFactory: buildTimeApiBaseUrl }'),
  'app.config.ts must provide the build-time API base URL through the token.',
);

const workflowSource = await readFile(
  join(taskRoot, '.github/workflows/deploy-static-web-app.yml'),
  'utf8',
);
check(workflowSource.includes('npm ci'), 'The SWA workflow must install dependencies explicitly.');
check(
  workflowSource.includes('skip_app_build: true') &&
    workflowSource.includes('app_location: day-17/task-1/web/dist/app/browser'),
  'The SWA workflow must upload the prebuilt browser artifact and skip Oryx.',
);
check(
  /azure_static_web_apps_api_token:\s*\$\{\{\s*secrets\./.test(workflowSource),
  'The SWA deployment token must be referenced through a GitHub secret.',
);

async function collectFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    if (entry.isDirectory() && ignoredDirectories.has(entry.name)) {
      continue;
    }

    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...(await collectFiles(path)));
    } else if (entry.isFile()) {
      files.push(path);
    }
  }

  return files;
}

const allowedSyntheticIdentifiers = new Set([
  '11111111-1111-1111-1111-111111111111',
  '22222222-2222-2222-2222-222222222222',
]);
const uuidPattern = /\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/gi;
const secretPatterns = [
  ['JWT value', /\beyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{10,}\b/],
  [
    'connection string',
    /DefaultEndpointsProtocol=|AccountKey=|SharedAccessSignature=/i,
  ],
  ['private key', /-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/],
  [
    'literal client secret',
    /(?:client[_-]?secret|ClientSecret)\s*[:=]\s*["'][^"'\n$<{][^"'\n]{7,}["']/i,
  ],
  [
    'literal signing key',
    /SigningKeyBase64["']?\s*[:=]\s*["'](?!YOUR_|<)[A-Za-z0-9+/=]{32,}["']/i,
  ],
];

for (const file of await collectFiles(taskRoot)) {
  const contents = await readFile(file, 'utf8').catch(() => null);
  if (contents === null) {
    continue;
  }

  const path = relative(taskRoot, file);
  // This script's own source necessarily contains the literal pattern text it
  // searches for (e.g. 'AccountKey='), which would otherwise flag itself.
  if (path !== 'scripts/verify-offline.mjs') {
    for (const [label, pattern] of secretPatterns) {
      check(!pattern.test(contents), `${path} contains a possible ${label}.`);
    }
  }

  const identifierScanContents = contents.replace(
    /<UserSecretsId>[0-9a-f-]+<\/UserSecretsId>/gi,
    '<UserSecretsId>LOCAL_DEVELOPMENT_IDENTIFIER</UserSecretsId>',
  );
  for (const identifier of identifierScanContents.match(uuidPattern) ?? []) {
    check(
      allowedSyntheticIdentifiers.has(identifier.toLowerCase()),
      `${path} contains a literal identifier instead of a placeholder.`,
    );
  }
}

if (failures.length > 0) {
  console.error(`Offline verification failed (${failures.length}):`);
  for (const failure of failures) {
    console.error(`- ${failure}`);
  }
  process.exitCode = 1;
} else {
  console.log('Offline verification passed: SPA fallback, injectable API base URL, Oryx bypass, and secret scan.');
}

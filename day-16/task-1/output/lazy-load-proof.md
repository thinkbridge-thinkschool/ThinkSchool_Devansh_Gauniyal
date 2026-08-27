# Lazy-loading proof — Step 4E

Real command output only. Full build logs are captured verbatim alongside this file:
`output/build-before-routing.txt` (before any routing was added) and
`output/build-after-routing.txt` (after).

## Before (no routing) — `ng build --configuration production`

```
Initial chunk files | Names         |  Raw size | Estimated transfer size
main-YHMPCF6S.js    | main          | 261.45 kB |                69.25 kB
styles-2CXFVZI3.css | styles        |  75 bytes |                75 bytes

                    | Initial total | 261.52 kB |                69.33 kB
```

## After (routing added) — `ng build --configuration production`

```
Initial chunk files | Names             |  Raw size | Estimated transfer size
chunk-VE5SX3A7.js   | -                 | 260.73 kB |                70.79 kB
main-WRLQ7LK7.js    | main              |  91.80 kB |                21.24 kB
styles-2CXFVZI3.css | styles            |  75 bytes |                75 bytes

                    | Initial total     | 352.61 kB |                92.11 kB

Lazy chunk files    | Names             |  Raw size | Estimated transfer size
chunk-5PWGBFH4.js   | quote-detail-page |   3.56 kB |                 1.22 kB
```

A separate, named lazy chunk (`chunk-5PWGBFH4.js`, name `quote-detail-page`) exists,
distinct from the initial chunks. `dist/app/browser/index.html` confirms only the two
initial files are referenced up front:

```
<link rel="modulepreload" href="chunk-VE5SX3A7.js"><script src="main-WRLQ7LK7.js" type="module"></script>
```

`chunk-5PWGBFH4.js` is not linked, preloaded, or referenced anywhere in `index.html` — it
is fetched only when the router actually activates the `quotes` / `quotes/:id` route.

Initial (eager) total grew by ~91 kB raw / ~23 kB transfer — that is the cost of adding
`@angular/router` itself (provideRouter, withComponentInputBinding, withViewTransitions)
to the whole app, which is genuinely used app-wide, not the detail page. The detail
page's own code (3.56 kB raw / 1.22 kB transfer) is the part that split out.

## Grep proof: the detail component's distinctive template text is absent from both eager chunks

`quote-detail-page.html`'s heading text, `Quote detail — routed page`, appears nowhere
else in the app and is a safe unique marker (the em dash makes it unambiguous).

```
$ grep -o "Quote detail" dist/app/browser/main-WRLQ7LK7.js dist/app/browser/chunk-VE5SX3A7.js
(no output, exit code 1 — no match in either eager file)

$ grep -o "Quote detail" dist/app/browser/chunk-5PWGBFH4.js
Quote detail
```

Present in the lazy chunk, absent from both files that load eagerly. The detail
component's real template content is genuinely not in the main bundle.

## The one expected exception: the loader glue, not the component

A broader grep for the class name shows one match in the main bundle:

```
$ grep -c "quote-detail-page\|QuoteDetailPage" dist/app/browser/main-WRLQ7LK7.js dist/app/browser/chunk-VE5SX3A7.js
dist/app/browser/main-WRLQ7LK7.js:1
dist/app/browser/chunk-VE5SX3A7.js:0

$ grep -o ".\{60\}QuoteDetailPage.\{60\}" dist/app/browser/main-WRLQ7LK7.js
],loadComponent:()=>import("./chunk-5PWGBFH4.js").then(n=>n.QuoteDetailPage)},{path:"quotes/:id",canActivate:[Pt],loadComponent:()=>imp
```

This is `app.routes.ts`'s own `loadComponent: () => import(...).then((m) =>
m.QuoteDetailPage)` line, compiled down to the route config that must ship in the main
bundle so the router knows which chunk to fetch and which named export to read off it
once it arrives. It is route-table wiring, not the component's implementation — the
component's actual body (decorator metadata, template, the `paramProblem`/`numericId`
logic) lives only in `chunk-5PWGBFH4.js`, as the first grep above shows. This is exactly
the distinction the task's own LAZY-LOADING PROOF section warns about: a route can look
lazy in source while still landing eagerly if something re-imports the component's
module directly (not just its name, as a string, inside the loader glue) — that did not
happen here; `scripts/verify-structural.mjs`'s new
"quote-detail-page is never statically imported outside its own directory" check backs
this up statically (see PROVENANCE.md / verification-log.md).

## Network-tab confirmation

This build-output proof is the primary evidence, per the task's own instructions ("You
cannot open a browser network tab... the PRIMARY evidence is the build"). The
browser network-tab confirmation is Devansh's own to perform — see
`verification-log.md` for the exact script and its current status (PENDING or recorded).

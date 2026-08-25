Take the Angular 21 app from day-14/task-1 — the quotes list-plus-detail with the reactive create-a-quote form — and copy it into day-14/task-2 untouched. Do not modify day-14/task-1 or anything from Day 13.

Inside day-14/task-2, rebuild the SAME create-a-quote form a second time using the Signal Forms preview API, alongside the existing reactive-forms version. Keep both. They must post to the same real endpoint with the same real fields, so the two can be compared directly.

The API is the QuotesApi at day-3/task-3/QuotesApi. Read the POST endpoint's route, its request DTO and every validation attribute on that DTO. The form shape and every validator must come from those actual fields and constraints. Do not invent a field, do not omit a required one, and do not pick a limit that differs from the API's.

Requirements:
- the Signal Forms version must use the actual Signal Forms API as it exists in the installed Angular 21 package — verify the import path and symbols from node_modules, do not assume them
- the same four states handled and observable: pristine, dirty, touched, and submitted; validators firing; error display; a clean submit and a failed submit
- validators matching the real API constraints exactly, each with a comment naming the constraint and the DTO file it came from
- inject() for every dependency, no constructor injection; no `any` anywhere
- do not degrade the existing reactive form, the list, the detail, or the Day 13 stale-response guard
- where the preview API cannot do something the reactive version does, say so plainly in code comments and in the comparison — do not hand-roll a workaround and present it as Signal Forms doing the work

Also write a short, honest comparison: where Signal Forms is simpler than reactive forms in this specific form, and where it is still rough. Ground every claim in something you actually hit while building, not in general reputation. Do not claim parity you did not observe.

Change nothing else. No refactors, no renames, no dependency upgrades, no restyling.

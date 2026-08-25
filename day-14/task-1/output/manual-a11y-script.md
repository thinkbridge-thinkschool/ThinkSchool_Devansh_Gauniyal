# Manual accessibility check — for Devansh

This agent cannot operate a screen reader or drive a real browser, so none of
this has been performed yet. Status: **PENDING**.

## How to run it

```bash
cd day-14/task-1
npx ng serve
```

Then open `http://localhost:4200/`.

## Script

1. Load the page. Tab through the quote list/detail area first (unchanged
   from Day 13) and confirm it still behaves as it did in that task.
2. Tab into the "Create a quote" form. Confirm focus lands on the "Quote
   text" textarea and VoiceOver (or your screen reader) announces the label
   "Quote text".
3. Tab again. Confirm focus lands on the "Save quote" button and it is
   announced as a button.
4. Shift+Tab back to the textarea. Confirm a visible focus outline at every
   stop.
5. With the textarea empty, activate "Save quote". Confirm focus moves back
   to the textarea automatically and the screen reader announces "Quote text
   is required" (or equivalent) without you having to search for it.
6. Type text and submit again. Confirm a busy/saving state is announced
   briefly, then a success message announces the quote was saved, and
   (separately) that it now appears in the list above without a page reload.
7. If you can simulate a server error, confirm that error text is announced
   automatically when it appears.

VoiceOver toggles with `Cmd+F5` on macOS.

## Recording the result

> PENDING — not yet performed.

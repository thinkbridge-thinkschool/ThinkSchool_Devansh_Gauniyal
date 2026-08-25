# Manual accessibility check — for Devansh

This agent cannot operate a screen reader or drive a real browser. Status:
**DONE — performed by Devansh**, see "Recording the result" below for his
own observation.

The graded a11y wiring is entirely in the "Create a quote" form (steps 3-8
below). Steps 1-2 just get you past the local-only sign-in gate, which is
dev tooling, not part of the graded exercise.

## How to run it

```bash
cd day-3/task-3/QuotesApi
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile --urls http://localhost:5080
```

In a second terminal:

```bash
cd day-14/task-1
npx ng serve --proxy-config proxy.conf.json
```

Then open `http://localhost:4200/`.

## Script

1. Load the page. You'll see only a "Sign in" card. Tab to the Email field,
   type `dev@local.test`; Tab to Password, type the local dev password;
   Tab to "Sign in" and activate it.
2. Confirm the page replaces the sign-in card with the real app: a
   "Signed in" bar, then "Create a quote" heading and form, then the
   Quotes browser below it.
3. Tab into the "Create a quote" form. Confirm focus lands on the "Quote
   text" textarea and VoiceOver (or your screen reader) announces the label
   "Quote text".
4. Tab again. Confirm focus lands on the "Author" field and its label is
   announced.
5. Tab again. Confirm focus lands on the "Save quote" button and it is
   announced as a button.
6. Shift+Tab back to the textarea. Confirm a visible focus outline at every
   stop.
7. With both fields empty, activate "Save quote". Confirm focus moves back
   to the "Quote text" field automatically and the screen reader announces
   "Quote text is required" (or equivalent) without you having to search
   for it.
8. Fill in the quote text only (leave Author blank) and submit again.
   Confirm focus now moves to the "Author" field instead, and "Author is
   required" is announced.
9. Fill in both fields and submit. Confirm a busy/saving state is announced
   briefly, then a success message announces the quote was saved, and
   (separately) that it now appears in the list below without a page
   reload.
10. If you can simulate a server error, confirm that error text is
    announced automatically when it appears.

VoiceOver toggles with `Cmd+F5` on macOS.

## Recording the result

**Devansh's own observation**, given verbatim: the VoiceOver pass "is also
making sense nothing idiotic" — he ran VoiceOver over the form and found the
announcements made sense, with nothing broken or confusing.

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CreateQuoteForm } from './create-quote-form';

function setText(fixture: ComponentFixture<CreateQuoteForm>, value: string): void {
  const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
  textarea.value = value;
  textarea.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function setAuthor(fixture: ComponentFixture<CreateQuoteForm>, value: string): void {
  const input = fixture.nativeElement.querySelector('#quote-author') as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function submit(fixture: ComponentFixture<CreateQuoteForm>): void {
  const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
  form.dispatchEvent(new Event('submit'));
  fixture.detectChanges();
}

function fillValidForm(fixture: ComponentFixture<CreateQuoteForm>, text: string, author: string): void {
  setText(fixture, text);
  setAuthor(fixture, author);
}

describe('CreateQuoteForm', () => {
  let fixture: ComponentFixture<CreateQuoteForm>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CreateQuoteForm],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(CreateQuoteForm);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  // --- The four states named by the task -----------------------------------

  it('EMPTY: a freshly rendered form shows no error message and no aria-invalid on either field', () => {
    const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    const author = fixture.nativeElement.querySelector('#quote-author') as HTMLInputElement;
    const textError = fixture.nativeElement.querySelector('#quote-text-error') as HTMLElement;
    const authorError = fixture.nativeElement.querySelector('#quote-author-error') as HTMLElement;

    expect(textarea.getAttribute('aria-invalid')).toBeNull();
    expect(textarea.getAttribute('aria-describedby')).toBeNull();
    expect(textError.textContent?.trim()).toBe('');
    expect(author.getAttribute('aria-invalid')).toBeNull();
    expect(author.getAttribute('aria-describedby')).toBeNull();
    expect(authorError.textContent?.trim()).toBe('');
  });

  it('INVALID: submitting empty marks both required fields invalid, renders both errors, sets aria-invalid, and does not call the API', () => {
    submit(fixture);

    const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    const author = fixture.nativeElement.querySelector('#quote-author') as HTMLInputElement;
    const textError = fixture.nativeElement.querySelector('#quote-text-error') as HTMLElement;
    const authorError = fixture.nativeElement.querySelector('#quote-author-error') as HTMLElement;

    expect(textarea.getAttribute('aria-invalid')).toBe('true');
    expect(textError.textContent?.trim()).toBe('Quote text is required.');
    expect(author.getAttribute('aria-invalid')).toBe('true');
    expect(authorError.textContent?.trim()).toBe('Author is required.');
    httpMock.expectNone('/api/quotes');
  });

  it('INVALID: text filled but author blank still blocks submission and marks only author invalid', () => {
    setText(fixture, 'Only text is filled in');
    submit(fixture);

    const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    const author = fixture.nativeElement.querySelector('#quote-author') as HTMLInputElement;

    expect(textarea.getAttribute('aria-invalid')).toBeNull();
    expect(author.getAttribute('aria-invalid')).toBe('true');
    httpMock.expectNone('/api/quotes');
  });

  it('SUBMITTING: the busy state is active in flight and the form cannot be double-submitted', () => {
    fillValidForm(fixture, 'A quote in flight', 'Some Author');
    submit(fixture);

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    const status = fixture.nativeElement.querySelector('.status') as HTMLElement;
    expect(button.disabled).toBe(true);
    expect(status.textContent).toContain('Submitting');

    // A second submit while the first request is still in flight must not
    // fire a second request -- expectOne below fails if more than one exists.
    submit(fixture);
    const req = httpMock.expectOne('/api/quotes');
    req.flush({ id: 9, ownerId: 'user-1', text: 'A quote in flight', author: 'Some Author' });
  });

  it('SERVER-ERROR: a failing POST surfaces a visible, announced error and leaves the form usable again', () => {
    fillValidForm(fixture, 'A quote that will be rejected', 'Some Author');
    submit(fixture);

    const req = httpMock.expectOne('/api/quotes');
    req.flush('nope', { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    const status = fixture.nativeElement.querySelector('.status') as HTMLElement;
    const serverError = fixture.nativeElement.querySelector('.server-error') as HTMLElement;

    expect(button.disabled).toBe(false);
    expect(status.textContent?.trim()).toBe('');
    expect(serverError.getAttribute('role')).toBe('alert');
    expect(serverError.textContent?.trim().length).toBeGreaterThan(0);
  });

  it('emits quoteCreated with the real created quote on success, for the host page to add to the list', () => {
    let emitted: unknown;
    fixture.componentInstance.quoteCreated.subscribe((quote) => {
      emitted = quote;
    });

    fillValidForm(fixture, 'A quote the host list should receive', 'Some Author');
    submit(fixture);

    const req = httpMock.expectOne('/api/quotes');
    req.flush({ id: 7, ownerId: 'user-1', text: 'A quote the host list should receive', author: 'Some Author' });

    expect(emitted).toEqual({
      id: 7,
      ownerId: 'user-1',
      text: 'A quote the host list should receive',
      author: 'Some Author',
    });
  });

  // --- Author (compulsory, at Devansh's explicit request, 2026-08-25) --------

  it('AUTHOR: is required -- a blank author blocks submission just like blank text', () => {
    setText(fixture, 'Text is filled in, author is not');
    submit(fixture);

    httpMock.expectNone('/api/quotes');
    const authorError = fixture.nativeElement.querySelector('#quote-author-error') as HTMLElement;
    expect(authorError.textContent?.trim()).toBe('Author is required.');
  });

  it('AUTHOR: a filled author is included in the payload under the real field name', () => {
    fillValidForm(fixture, 'A quote with an author', 'Marcus Aurelius');
    submit(fixture);

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.body).toEqual({ text: 'A quote with an author', author: 'Marcus Aurelius' });
    req.flush({ id: 2, ownerId: 'user-1', text: 'A quote with an author', author: 'Marcus Aurelius' });
  });

  // --- Contract tests --------------------------------------------------------

  it('CONTRACT: submits the real "text" and "author" fields, with the real field names and casing', () => {
    fillValidForm(fixture, 'Exact field name check', 'Some Author');
    submit(fixture);

    const req = httpMock.expectOne('/api/quotes');
    expect(Object.keys(req.request.body).sort()).toEqual(['author', 'text']);
    expect(req.request.body.text).toBe('Exact field name check');
    expect(req.request.body.author).toBe('Some Author');
    req.flush({ id: 1, ownerId: 'user-1', text: 'Exact field name check', author: 'Some Author' });
  });

  it('CONTRACT: does not invent a maxLength the real DTO does not have, on either field', () => {
    // day-3/task-3/QuotesApi/Quotes/QuoteRequests.cs:
    //   public sealed record CreateQuoteRequest(string Text, string? Author = null);
    // carries no [MaxLength]/[StringLength] attribute on either field, so an
    // arbitrarily long value must not be rejected client-side.
    setText(fixture, 'x'.repeat(5000));
    setAuthor(fixture, 'y'.repeat(2000));
    const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    const author = fixture.nativeElement.querySelector('#quote-author') as HTMLInputElement;
    expect(textarea.getAttribute('aria-invalid')).toBeNull();
    expect(author.getAttribute('aria-invalid')).toBeNull();
  });

  // --- Accessibility ----------------------------------------------------------

  it('A11Y: every input (text and author) has a label whose for matches its id', () => {
    const labels = fixture.nativeElement.querySelectorAll('label') as NodeListOf<HTMLLabelElement>;
    expect(labels.length).toBe(2);
    labels.forEach((label) => {
      const targetId = label.getAttribute('for');
      expect(targetId).toBeTruthy();
      expect(fixture.nativeElement.querySelector(`#${targetId}`)).toBeTruthy();
    });
  });

  it('A11Y: aria-describedby resolves to an element present in the DOM in both the valid and invalid state, for both fields', () => {
    const textareaValid = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    const authorValid = fixture.nativeElement.querySelector('#quote-author') as HTMLInputElement;
    for (const el of [textareaValid, authorValid]) {
      const validRef = el.getAttribute('aria-describedby');
      if (validRef) {
        expect(fixture.nativeElement.querySelector(`#${validRef}`)).toBeTruthy();
      }
    }

    submit(fixture);

    const textareaInvalid = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    const authorInvalid = fixture.nativeElement.querySelector('#quote-author') as HTMLInputElement;
    expect(textareaInvalid.getAttribute('aria-describedby')).toBe('quote-text-error');
    expect(authorInvalid.getAttribute('aria-describedby')).toBe('quote-author-error');
    expect(fixture.nativeElement.querySelector('#quote-text-error')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('#quote-author-error')).toBeTruthy();
  });

  it('A11Y: focus moves to the first invalid field (text) after submitting completely empty', () => {
    document.body.appendChild(fixture.nativeElement);
    submit(fixture);
    const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    expect(document.activeElement).toBe(textarea);
    fixture.nativeElement.remove();
  });

  it('A11Y: focus moves to author when text is valid but author is the only invalid field', () => {
    document.body.appendChild(fixture.nativeElement);
    setText(fixture, 'Text is valid');
    submit(fixture);
    const author = fixture.nativeElement.querySelector('#quote-author') as HTMLInputElement;
    expect(document.activeElement).toBe(author);
    fixture.nativeElement.remove();
  });

  it('A11Y: no element has a positive tabindex', () => {
    const withTabIndex = fixture.nativeElement.querySelectorAll('[tabindex]');
    withTabIndex.forEach((el: Element) => {
      expect(Number(el.getAttribute('tabindex'))).toBeLessThanOrEqual(0);
    });
  });

  it('A11Y: the textarea, author input, and submit button are reachable in the tab order in the initial state', () => {
    const textarea = fixture.nativeElement.querySelector('#quote-text') as HTMLTextAreaElement;
    const author = fixture.nativeElement.querySelector('#quote-author') as HTMLInputElement;
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(textarea.disabled).toBe(false);
    expect(author.disabled).toBe(false);
    expect(button.disabled).toBe(false);
    expect(textarea.hasAttribute('tabindex')).toBe(false);
    expect(author.hasAttribute('tabindex')).toBe(false);
    expect(button.hasAttribute('tabindex')).toBe(false);
  });
});

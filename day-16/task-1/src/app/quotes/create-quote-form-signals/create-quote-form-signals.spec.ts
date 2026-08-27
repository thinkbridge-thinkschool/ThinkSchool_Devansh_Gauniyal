import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CreateQuoteFormSignals } from './create-quote-form-signals';

function setText(fixture: ComponentFixture<CreateQuoteFormSignals>, value: string): void {
  const textarea = fixture.nativeElement.querySelector('#quote-text-signals') as HTMLTextAreaElement;
  textarea.value = value;
  textarea.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function setAuthor(fixture: ComponentFixture<CreateQuoteFormSignals>, value: string): void {
  const input = fixture.nativeElement.querySelector('#quote-author-signals') as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function blur(fixture: ComponentFixture<CreateQuoteFormSignals>, selector: string): void {
  // Signal Forms' FormField directive marks a field touched on the native
  // 'blur' event specifically (verified by reading
  // node_modules/@angular/forms/fesm2022/signals.mjs:
  // `host.listenToDom('blur', () => parent.state().markAsTouched())`) --
  // 'focusout' does NOT trigger it, even though it bubbles the same way.
  // See verification-log.md for the real test failure this correction fixed.
  const el = fixture.nativeElement.querySelector(selector) as HTMLElement;
  el.dispatchEvent(new Event('focus'));
  fixture.detectChanges();
  el.dispatchEvent(new Event('blur'));
  fixture.detectChanges();
}

function submitForm(fixture: ComponentFixture<CreateQuoteFormSignals>): void {
  const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
  form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
  fixture.detectChanges();
}

describe('CreateQuoteFormSignals', () => {
  let fixture: ComponentFixture<CreateQuoteFormSignals>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CreateQuoteFormSignals],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(CreateQuoteFormSignals);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  // --- The states the task names: pristine, dirty, touched, validators, submitted ---

  it('PRISTINE: a freshly rendered form reports pristine (not dirty) on both fields and shows no error', () => {
    const component = fixture.componentInstance;
    expect(component.quoteForm.text().dirty()).toBe(false);
    expect(component.quoteForm.author().dirty()).toBe(false);

    const textarea = fixture.nativeElement.querySelector('#quote-text-signals') as HTMLTextAreaElement;
    const author = fixture.nativeElement.querySelector('#quote-author-signals') as HTMLInputElement;
    expect(textarea.getAttribute('aria-invalid')).toBeNull();
    expect(author.getAttribute('aria-invalid')).toBeNull();
    const textError = fixture.nativeElement.querySelector('#quote-text-signals-error') as HTMLElement;
    expect(textError.textContent?.trim()).toBe('');
  });

  it('DIRTY: changing the text value flips text to dirty without affecting author', () => {
    const component = fixture.componentInstance;
    setText(fixture, 'A new quote');
    expect(component.quoteForm.text().dirty()).toBe(true);
    expect(component.quoteForm.author().dirty()).toBe(false);
  });

  it('TOUCHED: focusing then blurring a control marks it touched, and only that one', () => {
    const component = fixture.componentInstance;
    expect(component.quoteForm.text().touched()).toBe(false);
    blur(fixture, '#quote-text-signals');
    expect(component.quoteForm.text().touched()).toBe(true);
    expect(component.quoteForm.author().touched()).toBe(false);
  });

  it('VALIDATORS FIRING: an empty text field reports the real required error, matching day-3/task-3/QuotesApi\'s CreateQuoteRequest.Text having no default', () => {
    const component = fixture.componentInstance;
    blur(fixture, '#quote-text-signals');
    const errors = component.quoteForm.text().errors();
    expect(errors.length).toBe(1);
    expect(errors[0].kind).toBe('required');
  });

  it('ERROR DISPLAY: the error message renders only after touched, never on a pristine form', () => {
    const textErrorPristine = fixture.nativeElement.querySelector('#quote-text-signals-error') as HTMLElement;
    expect(textErrorPristine.textContent?.trim()).toBe('');

    blur(fixture, '#quote-text-signals');
    const textErrorTouched = fixture.nativeElement.querySelector('#quote-text-signals-error') as HTMLElement;
    expect(textErrorTouched.textContent?.trim()).toBe('Quote text is required.');
  });

  it('CLEAN SUBMIT: a valid form issues a POST to the real route with the real field names and exact casing', () => {
    setText(fixture, 'Security is a process.');
    setAuthor(fixture, 'Marcus Aurelius');
    submitForm(fixture);

    const req = httpMock.expectOne('/api/quotes');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ text: 'Security is a process.', author: 'Marcus Aurelius' });
    req.flush({ id: 1, ownerId: 'user-1', text: 'Security is a process.', author: 'Marcus Aurelius' });
  });

  it('SUBMITTED (invalid): submitting a completely empty form marks both fields touched, does not call the API, and moves focus to the first invalid field', () => {
    document.body.appendChild(fixture.nativeElement);
    submitForm(fixture);

    const component = fixture.componentInstance;
    expect(component.quoteForm.text().touched()).toBe(true);
    expect(component.quoteForm.author().touched()).toBe(true);
    httpMock.expectNone('/api/quotes');

    const textarea = fixture.nativeElement.querySelector('#quote-text-signals') as HTMLTextAreaElement;
    expect(document.activeElement).toBe(textarea);
    fixture.nativeElement.remove();
  });

  it('SUBMITTED (invalid, text valid but author blank): focus moves to author instead', () => {
    document.body.appendChild(fixture.nativeElement);
    setText(fixture, 'Only text is filled in');
    submitForm(fixture);

    const author = fixture.nativeElement.querySelector('#quote-author-signals') as HTMLInputElement;
    expect(document.activeElement).toBe(author);
    fixture.nativeElement.remove();
  });

  it('FAILED SUBMIT: a rejected POST surfaces the error and does not leave the form stuck submitting', async () => {
    setText(fixture, 'A quote that will be rejected');
    setAuthor(fixture, 'Someone');
    submitForm(fixture);

    const component = fixture.componentInstance;
    const req = httpMock.expectOne('/api/quotes');
    req.flush('nope', { status: 500, statusText: 'Internal Server Error' });
    // The action wraps the QuoteApi Observable in a hand-rolled `new Promise`,
    // which this zoneless app's `ApplicationRef` pending-task tracking does
    // not see -- `await fixture.whenStable()` alone was observed to resolve
    // before that promise's microtasks actually ran (see verification-log.md
    // for the real, repeated test failure this fixes). A macrotask tick
    // reliably drains them.
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();

    expect(component.quoteForm().submitting()).toBe(false);
    const serverError = fixture.nativeElement.querySelector('.server-error') as HTMLElement;
    expect(serverError.getAttribute('role')).toBe('alert');
    expect(serverError.textContent?.trim().length).toBeGreaterThan(0);
  });

  // --- Contract tests --------------------------------------------------------

  it('CONTRACT: does not invent a maxLength the real DTO does not have, on either field', () => {
    setText(fixture, 'x'.repeat(5000));
    setAuthor(fixture, 'y'.repeat(2000));
    const component = fixture.componentInstance;
    expect(component.quoteForm.text().invalid()).toBe(false);
    expect(component.quoteForm.author().invalid()).toBe(false);
  });

  // --- Accessibility -----------------------------------------------------------

  it('A11Y: every input has a label whose for matches its id', () => {
    const labels = fixture.nativeElement.querySelectorAll('label') as NodeListOf<HTMLLabelElement>;
    expect(labels.length).toBe(2);
    labels.forEach((label) => {
      const targetId = label.getAttribute('for');
      expect(targetId).toBeTruthy();
      expect(fixture.nativeElement.querySelector(`#${targetId}`)).toBeTruthy();
    });
  });

  it('A11Y: aria-describedby resolves to an element present in the DOM in both the valid and invalid state', () => {
    const textareaValid = fixture.nativeElement.querySelector('#quote-text-signals') as HTMLTextAreaElement;
    const validRef = textareaValid.getAttribute('aria-describedby');
    if (validRef) {
      expect(fixture.nativeElement.querySelector(`#${validRef}`)).toBeTruthy();
    }

    blur(fixture, '#quote-text-signals');
    const textareaInvalid = fixture.nativeElement.querySelector('#quote-text-signals') as HTMLTextAreaElement;
    expect(textareaInvalid.getAttribute('aria-describedby')).toBe('quote-text-signals-error');
    expect(fixture.nativeElement.querySelector('#quote-text-signals-error')).toBeTruthy();
  });

  it('A11Y: no element has a positive tabindex', () => {
    const withTabIndex = fixture.nativeElement.querySelectorAll('[tabindex]');
    withTabIndex.forEach((el: Element) => {
      expect(Number(el.getAttribute('tabindex'))).toBeLessThanOrEqual(0);
    });
  });
});

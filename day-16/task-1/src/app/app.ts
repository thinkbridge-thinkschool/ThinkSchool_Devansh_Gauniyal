/**
 * Thin root shell: everything App used to render directly now lives in HomePage
 * ('/') and LoginPage ('/login') -- see app.routes.ts. This lets '/login' and '/' be
 * real, separate, bookmarkable URLs instead of a same-URL signal-driven show/hide.
 */
import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {}

import { Component, ElementRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RaceApi, Split, problemMessage } from './race-api';
import { RaceStore } from './race-store';

/**
 * The screen a volunteer uses at a checkpoint: type a bib, press Enter, glance at the name.
 * Built for speed and for mistakes: the input keeps focus, every entry shows who it matched,
 * and a wrong bib can be undone from the list.
 */
@Component({
  selector: 'app-entry',
  imports: [FormsModule, DatePipe],
  template: `
    <div class="entry-head">
      <label>
        Checkpoint
        <select [ngModel]="checkpointId()" (ngModelChange)="checkpointId.set(+$event)">
          @for (c of race()?.checkpoints ?? []; track c.id) { <option [value]="c.id">{{ c.name }}</option> }
        </select>
      </label>
      <label>
        Volunteer
        <input [(ngModel)]="volunteer" placeholder="your name" />
      </label>
    </div>

    <form class="bib-form" (ngSubmit)="record()">
      <input #bibInput class="bib" type="text" inputmode="numeric" autocomplete="off" placeholder="Bib #"
             [(ngModel)]="bib" name="bib" [readOnly]="busy()"
             (keydown.enter)="$event.preventDefault(); record()" />
      <button class="primary" type="submit" [disabled]="busy() || !bib">Record</button>
    </form>

    @if (feedback(); as f) {
      <div class="alert" [class.ok]="f.kind === 'ok'" [class.warn]="f.kind === 'warn'" [class.error]="f.kind === 'error'">{{ f.text }}</div>
    }

    <h3>Recent at this checkpoint</h3>
    <table class="grid compact">
      <thead><tr><th>Time</th><th>Bib</th><th>Runner</th><th>By</th><th></th></tr></thead>
      <tbody>
        @for (s of recent(); track s.id) {
          <tr>
            <td class="mono">{{ s.time | date: 'h:mm:ss a' }}</td>
            <td>{{ s.bib }}</td>
            <td>{{ s.runnerName ?? 'Unknown bib' }}</td>
            <td class="muted">{{ s.recordedBy }}</td>
            <td class="num"><button class="link" (click)="undo(s)">Undo</button></td>
          </tr>
        } @empty {
          <tr><td colspan="5" class="muted">No entries yet.</td></tr>
        }
      </tbody>
    </table>
  `,
})
export class EntryComponent {
  private api = inject(RaceApi);
  private store = inject(RaceStore);
  readonly raceId = input.required<number, string>({ transform: Number });
  readonly race = computed(() => this.store.races().find(r => r.id === this.raceId()));

  readonly checkpointId = signal(0);
  readonly recent = signal<Split[]>([]);
  readonly busy = signal(false);
  readonly feedback = signal<{ kind: 'ok' | 'warn' | 'error'; text: string } | null>(null);
  private readonly bibInput = viewChild.required<ElementRef<HTMLInputElement>>('bibInput');

  bib = '';
  volunteer = localStorage.getItem('volunteer') ?? '';

  constructor() {
    // Default to the first checkpoint whenever the race changes.
    effect(() => {
      const first = this.race()?.checkpoints[0]?.id;
      if (first && !this.race()!.checkpoints.some(c => c.id === this.checkpointId())) this.checkpointId.set(first);
    });
    effect(() => { if (this.checkpointId()) this.loadRecent(); });
  }

  record() {
    if (this.busy()) return; // a held-down Enter key shouldn't record the same runner twice
    const bib = Number(this.bib.trim());
    if (!Number.isInteger(bib) || bib <= 0) {
      this.feedback.set({ kind: 'error', text: 'Enter a bib number.' });
      return;
    }
    localStorage.setItem('volunteer', this.volunteer);
    this.busy.set(true);
    this.api.recordSplit(this.raceId(), bib, this.checkpointId(), this.volunteer).subscribe({
      next: s => {
        this.feedback.set(s.warning
          ? { kind: 'warn', text: `#${s.bib} ${s.runnerName}: ${s.warning}` }
          : { kind: 'ok', text: `#${s.bib} ${s.runnerName} recorded at ${s.checkpoint}` });
        this.bib = '';
        this.loadRecent();
        this.done();
      },
      error: e => {
        this.feedback.set({ kind: 'error', text: problemMessage(e) });
        this.done(); // keep the bib so it can be corrected
      },
    });
  }

  undo(s: Split) {
    this.api.deleteSplit(this.raceId(), s.id).subscribe({
      next: () => {
        this.feedback.set({ kind: 'ok', text: `Removed #${s.bib} recorded at ${new Date(s.time).toLocaleTimeString()}` });
        this.loadRecent();
      },
      error: e => this.feedback.set({ kind: 'error', text: problemMessage(e) }),
    });
  }

  private done() {
    this.busy.set(false);
    setTimeout(() => this.bibInput().nativeElement.focus());
  }

  private loadRecent() {
    this.api.recentSplits(this.raceId(), this.checkpointId()).subscribe(r => this.recent.set(r));
  }
}

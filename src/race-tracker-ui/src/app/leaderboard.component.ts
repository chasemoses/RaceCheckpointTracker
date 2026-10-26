import { Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, combineLatest, of, switchMap, timer } from 'rxjs';
import { DurationPipe } from './duration.pipe';
import { RaceApi } from './race-api';

@Component({
  selector: 'app-leaderboard',
  imports: [RouterLink, DurationPipe],
  template: `
    <div class="toolbar">
      <div class="counts">
        <span><strong>{{ results()?.finished ?? '–' }}</strong> finished</span>
        <span><strong>{{ results()?.onCourse ?? '–' }}</strong> on course</span>
        <span><strong>{{ results()?.notStarted ?? '–' }}</strong> not started</span>
      </div>
      <label>
        Division
        <select [value]="division()" (change)="division.set($any($event.target).value)">
          <option value="">All</option>
          @for (d of divisions(); track d) { <option [value]="d">{{ d }}</option> }
        </select>
      </label>
    </div>

    @if (error()) { <div class="alert error">Couldn’t refresh results. Retrying…</div> }

    <table class="grid">
      <thead>
        <tr><th>Place</th><th>Bib</th><th>Runner</th><th>Division</th><th class="num">Time</th><th class="num">Pace /km</th><th>Status</th></tr>
      </thead>
      <tbody>
        @for (r of results()?.rows ?? []; track r.bib) {
          <tr [class.dim]="r.status === 'NotStarted'">
            <td>{{ r.place ?? '' }}@if (r.divisionPlace) { <span class="muted small"> ({{ r.divisionPlace }})</span> }</td>
            <td><a [routerLink]="['/races', raceId(), 'runners', r.bib]">{{ r.bib }}</a></td>
            <td>{{ r.name }}</td>
            <td class="muted">{{ r.division }}</td>
            <td class="num mono">{{ (r.finishSeconds ?? r.lastSeenSeconds) | duration }}</td>
            <td class="num mono">{{ r.paceSecondsPerKm | duration }}</td>
            <td>
              @switch (r.status) {
                @case ('Finished') { <span class="badge ok">Finished</span> }
                @case ('OnCourse') { <span class="badge live">{{ r.lastSeenAt }}</span> }
                @default { <span class="badge">Not started</span> }
              }
              @if (r.needsReview) { <span class="badge warn" title="Finished with a missing checkpoint">Review</span> }
            </td>
          </tr>
        }
      </tbody>
    </table>
    <p class="muted small">Updates every 5 seconds. Division place in parentheses. “Review” means a finisher missed a checkpoint: a volunteer may have missed them, or they cut the course.</p>
  `,
})
export class LeaderboardComponent {
  private api = inject(RaceApi);
  readonly raceId = input.required<number, string>({ transform: Number });
  readonly division = signal('');
  readonly error = signal(false);

  // Re-fetch every 5 s, and immediately when the race or division changes.
  // switchMap cancels an in-flight request if a newer one starts, so results never arrive out of order.
  readonly results = toSignal(
    combineLatest([toObservable(this.raceId), toObservable(this.division)]).pipe(
      switchMap(([raceId, division]) => timer(0, 5000).pipe(
        switchMap(() => this.api.results(raceId, division || undefined).pipe(
          catchError(() => { this.error.set(true); return of(undefined); }),
        )),
      )),
    ),
  );

  private readonly allDivisions = signal<string[]>([]);
  readonly divisions = computed(() => {
    const seen = new Set([...this.allDivisions(), ...(this.results()?.rows ?? []).map(r => r.division)]);
    return [...seen].sort();
  });

  constructor() {
    // Remember divisions from the unfiltered list so the dropdown doesn't shrink when filtered.
    toObservable(this.results).subscribe(r => {
      if (r) { this.error.set(false); }
      if (r && !this.division()) this.allDivisions.set([...new Set(r.rows.map(x => x.division))]);
    });
  }
}


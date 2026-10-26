import { Component, inject, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { combineLatest, switchMap } from 'rxjs';
import { DurationPipe } from './duration.pipe';
import { RaceApi } from './race-api';

@Component({
  selector: 'app-runner',
  imports: [RouterLink, DurationPipe, DatePipe],
  template: `
    <p><a [routerLink]="['/races', raceId(), 'results']">← Leaderboard</a></p>
    @if (detail(); as d) {
      <h2>#{{ d.result.bib }} {{ d.result.name }} <span class="muted">{{ d.result.division }}</span></h2>
      <p>
        @if (d.result.status === 'Finished') {
          Finished in <strong class="mono">{{ d.result.finishSeconds | duration }}</strong>, place {{ d.result.place }}
          ({{ d.result.divisionPlace }} in division), {{ d.result.paceSecondsPerKm | duration }}/km.
        } @else if (d.result.status === 'OnCourse') {
          On course. Last seen at {{ d.result.lastSeenAt }} at <span class="mono">{{ d.result.lastSeenSeconds | duration }}</span>.
        } @else {
          No crossings recorded.
        }
      </p>

      @if (d.missed.length) {
        <div class="alert warn">No crossing recorded at: {{ d.missed.join(', ') }}.
          @if (d.result.needsReview) { Check with the volunteers at those checkpoints before posting results. }
        </div>
      }

      <table class="grid compact">
        <thead><tr><th>Lap</th><th>Checkpoint</th><th class="num">km</th><th>Clock time</th><th class="num">Elapsed</th></tr></thead>
        <tbody>
          @for (p of d.passes; track $index) {
            <tr [class.dim]="!p.time">
              <td>{{ p.lap }}</td>
              <td>{{ p.checkpoint }}</td>
              <td class="num">{{ p.distanceKm }}</td>
              <td class="mono">{{ p.time ? (p.time | date: 'h:mm:ss a') : '—' }}</td>
              <td class="num mono">{{ p.elapsedSeconds | duration }}</td>
            </tr>
          }
        </tbody>
      </table>

      @if (d.ignored.length) {
        <h3>Entries not counted</h3>
        <ul class="small">
          @for (i of d.ignored; track i.splitId) {
            <li>{{ i.checkpoint }} at <span class="mono">{{ i.time | date: 'h:mm:ss a' }}</span>: {{ i.issue }}</li>
          }
        </ul>
      }
    }
  `,
})
export class RunnerComponent {
  private api = inject(RaceApi);
  readonly raceId = input.required<number, string>({ transform: Number });
  readonly bib = input.required<number, string>({ transform: Number });

  readonly detail = toSignal(
    combineLatest([toObservable(this.raceId), toObservable(this.bib)]).pipe(
      switchMap(([raceId, bib]) => this.api.runner(raceId, bib)),
    ),
  );
}

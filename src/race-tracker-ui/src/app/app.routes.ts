import { Routes } from '@angular/router';
import { EntryComponent } from './entry.component';
import { HomeComponent } from './home.component';
import { LeaderboardComponent } from './leaderboard.component';
import { RunnerComponent } from './runner.component';

// Route parameters bind straight to component inputs (withComponentInputBinding).
export const routes: Routes = [
  { path: '', component: HomeComponent, title: 'Races' },
  { path: 'races/:raceId/results', component: LeaderboardComponent, title: 'Leaderboard' },
  { path: 'races/:raceId/entry', component: EntryComponent, title: 'Checkpoint entry' },
  { path: 'races/:raceId/runners/:bib', component: RunnerComponent, title: 'Runner' },
  { path: '**', redirectTo: '' },
];

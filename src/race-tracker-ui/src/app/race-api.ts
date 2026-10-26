import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// Shapes returned by the .NET API (see Contracts.cs).

export interface Checkpoint { id: number; name: string; sequence: number; distanceKm: number; }

export interface Race {
  id: number; name: string; route: 'Linear' | 'Loop'; startTime: string;
  laps: number; totalDistanceKm: number; runnerCount: number; checkpoints: Checkpoint[];
}

export interface ResultRow {
  bib: number; name: string; division: string; status: 'Finished' | 'OnCourse' | 'NotStarted';
  place: number | null; divisionPlace: number | null; finishSeconds: number | null;
  lastSeenSeconds: number | null; lastSeenAt: string | null; distanceKm: number;
  paceSecondsPerKm: number | null; needsReview: boolean; missedCount: number; ignoredCount: number;
}

export interface Results {
  raceId: number; calculatedAt: string; finished: number; onCourse: number; notStarted: number; rows: ResultRow[];
}

export interface Pass { lap: number; checkpoint: string; distanceKm: number; time: string | null; elapsedSeconds: number | null; }
export interface IgnoredSplit { splitId: number; checkpoint: string; time: string; issue: string; }
export interface RunnerDetail { result: ResultRow; passes: Pass[]; missed: string[]; ignored: IgnoredSplit[]; }

export interface Split {
  id: number; bib: number; runnerName: string | null; checkpoint: string; time: string; recordedBy: string; warning: string | null;
}

@Injectable({ providedIn: 'root' })
export class RaceApi {
  private http = inject(HttpClient);

  races(): Observable<Race[]> {
    return this.http.get<Race[]>('/api/races');
  }

  results(raceId: number, division?: string): Observable<Results> {
    const params: Record<string, string> = division ? { division } : {};
    return this.http.get<Results>(`/api/races/${raceId}/results`, { params });
  }

  runner(raceId: number, bib: number): Observable<RunnerDetail> {
    return this.http.get<RunnerDetail>(`/api/races/${raceId}/runners/${bib}`);
  }

  recentSplits(raceId: number, checkpointId: number): Observable<Split[]> {
    return this.http.get<Split[]>(`/api/races/${raceId}/checkpoints/${checkpointId}/splits`);
  }

  recordSplit(raceId: number, bib: number, checkpointId: number, recordedBy: string): Observable<Split> {
    return this.http.post<Split>(`/api/races/${raceId}/splits`, { bib, checkpointId, recordedBy });
  }

  deleteSplit(raceId: number, splitId: number): Observable<void> {
    return this.http.delete<void>(`/api/races/${raceId}/splits/${splitId}`);
  }
}

/** Pulls the first readable message out of an ASP.NET Core ProblemDetails response. */
export function problemMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const body = error.error;
    const first = body?.errors && Object.values(body.errors as Record<string, string[]>)[0]?.[0];
    if (first) return first;
    if (body?.title) return body.title;
    if (error.status === 0) return 'Can’t reach the server. Check the connection and try again.';
  }
  return 'Something went wrong. Try again.';
}

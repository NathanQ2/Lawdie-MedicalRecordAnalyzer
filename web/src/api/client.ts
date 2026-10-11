import type { CaseAnalysis, CaseDetail, CaseListItem, DemandDraft, JobStatus, RecordInfo, ReviewItem, ReviewStatus } from './types'

// The API rejects changes without this header; other websites cannot send it to a local server.
export const CLIENT_HEADER = 'X-MRA-Client'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set(CLIENT_HEADER, 'web')

  const response = await fetch(path, { ...init, headers })
  if (!response.ok) {
    let message = `Request failed (${response.status}).`
    try {
      const body = (await response.json()) as { error?: string }
      if (body.error) {
        message = body.error
      }
    } catch {
      // The response had no JSON error body; keep the generic message.
    }
    throw new ApiError(response.status, message)
  }

  const text = await response.text()
  return (text ? JSON.parse(text) : null) as T
}

function json(method: string, body: unknown): RequestInit {
  return { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
}

const casePath = (caseId: string) => `/api/cases/${encodeURIComponent(caseId)}`

export const api = {
  listCases: () => request<CaseListItem[]>('/api/cases'),
  createCase: (name: string) => request<CaseDetail>('/api/cases', json('POST', { name })),
  getCase: (caseId: string) => request<CaseDetail>(casePath(caseId)),
  deleteCase: (caseId: string) => request<null>(casePath(caseId), { method: 'DELETE' }),

  uploadRecords: (caseId: string, files: File[]) => {
    const form = new FormData()
    files.forEach((file) => form.append('files', file, file.name))
    return request<RecordInfo[]>(`${casePath(caseId)}/records`, { method: 'POST', body: form })
  },
  deleteRecord: (caseId: string, fileName: string) =>
    request<null>(`${casePath(caseId)}/records/${encodeURIComponent(fileName)}`, { method: 'DELETE' }),
  recordUrl: (caseId: string, fileName: string) => `${casePath(caseId)}/records/${encodeURIComponent(fileName)}`,

  startIngest: (caseId: string) => request<JobStatus>(`${casePath(caseId)}/ingest`, { method: 'POST' }),
  startDraft: (caseId: string) => request<JobStatus>(`${casePath(caseId)}/draft`, { method: 'POST' }),
  getCurrentJob: (caseId: string) => request<JobStatus | null>(`${casePath(caseId)}/jobs/current`),
  cancelJob: (caseId: string) => request<null>(`${casePath(caseId)}/jobs/cancel`, { method: 'POST' }),

  /** The case's analysis, or null if it has not been ingested yet. */
  getAnalysis: (caseId: string) => orNullIfMissing(request<CaseAnalysis>(`${casePath(caseId)}/analysis`)),
  /** The case's draft, or null if it has not been drafted yet. */
  getDraft: (caseId: string) => orNullIfMissing(request<DemandDraft>(`${casePath(caseId)}/draft`)),
  draftExportUrl: (caseId: string, format: 'docx' | 'md' | 'txt') => `${casePath(caseId)}/draft/export?format=${format}`,

  getReview: (caseId: string) => request<ReviewItem[]>(`${casePath(caseId)}/review`),
  updateReview: (caseId: string, itemId: string, status: ReviewStatus, note: string | null) =>
    request<ReviewItem>(`${casePath(caseId)}/review/${encodeURIComponent(itemId)}`, json('PUT', { status, note })),
}

async function orNullIfMissing<T>(pending: Promise<T>): Promise<T | null> {
  try {
    return await pending
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      return null
    }
    throw error
  }
}

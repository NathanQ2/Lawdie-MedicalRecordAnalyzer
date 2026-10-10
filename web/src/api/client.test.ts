import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, CLIENT_HEADER, api } from './client'

function mockFetch(response: Response) {
  const fetchMock = vi.fn().mockResolvedValue(response)
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

afterEach(() => vi.unstubAllGlobals())

describe('api client', () => {
  it('sends the client header the API requires', async () => {
    const fetchMock = mockFetch(new Response(JSON.stringify({ id: 'abc' }), { status: 201 }))

    await api.createCase('Ellis')

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/cases')
    expect(new Headers(init.headers).get(CLIENT_HEADER)).toBe('web')
    expect(init.body).toBe(JSON.stringify({ name: 'Ellis' }))
  })

  it('raises the API error message', async () => {
    mockFetch(new Response(JSON.stringify({ error: 'Run ingest before drafting.' }), { status: 409 }))

    await expect(api.startDraft('abc')).rejects.toEqual(new ApiError(409, 'Run ingest before drafting.'))
  })

  it('falls back to a generic message without a JSON body', async () => {
    mockFetch(new Response('Bad gateway', { status: 502 }))

    await expect(api.listCases()).rejects.toThrow('Request failed (502).')
  })

  it('returns null when there is no current job', async () => {
    mockFetch(new Response(null, { status: 204 }))

    await expect(api.getCurrentJob('abc')).resolves.toBeNull()
  })

  it('encodes record names in paths', () => {
    expect(api.recordUrl('abc', 'ED record #1.pdf')).toBe('/api/cases/abc/records/ED%20record%20%231.pdf')
  })
})

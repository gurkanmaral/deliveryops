const coreApiUrl = import.meta.env.VITE_CORE_API_URL ?? 'http://localhost:5100'
const authApiUrl = import.meta.env.VITE_AUTH_API_URL ?? 'http://localhost:5200'
const integrationsApiUrl = import.meta.env.VITE_INTEGRATIONS_API_URL ?? 'http://localhost:5400'

let accessToken: string | null = null
let refreshPromise: Promise<string> | null = null
let authenticationLostListener: (() => void) | null = null
export function setAccessToken(token: string | null) { accessToken = token }
export function getAccessToken() { return accessToken }
export function getCoreApiUrl() { return coreApiUrl }
export function getIntegrationsApiUrl() { return integrationsApiUrl }
export function onAuthenticationLost(listener: () => void) {
  authenticationLostListener = listener
  return () => { if (authenticationLostListener === listener) authenticationLostListener = null }
}

export class ApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message)
    this.name = 'ApiError'
  }
}

export type ApiTarget = 'core' | 'auth' | 'integrations'
interface RequestOptions extends RequestInit { api?: ApiTarget }
interface RefreshResponse { accessToken: string }

function send(path: string, options: RequestOptions) {
  const { api = 'core', headers, ...requestOptions } = options
  const baseUrl = api === 'auth' ? authApiUrl : api === 'integrations' ? integrationsApiUrl : coreApiUrl
  return fetch(`${baseUrl}${path}`, {
    ...requestOptions,
    credentials: api === 'auth' ? 'include' : 'same-origin',
    headers: {
      Accept: 'application/json',
      ...(requestOptions.body ? { 'Content-Type': 'application/json' } : {}),
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...headers,
    },
  })
}

async function parse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; title?: string; errors?: Record<string, string[]> } | null
    const validationMessage = problem?.errors ? Object.values(problem.errors).flat()[0] : undefined
    throw new ApiError(response.status, validationMessage ?? problem?.detail ?? problem?.title ?? `API isteği başarısız oldu (${response.status}).`)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

async function renewAccessToken() {
  if (!refreshPromise) {
    refreshPromise = (async () => {
      const response = await send('/api/v1/auth/refresh', { api: 'auth', method: 'POST' })
      const session = await parse<RefreshResponse>(response)
      setAccessToken(session.accessToken)
      return session.accessToken
    })().finally(() => { refreshPromise = null })
  }
  return refreshPromise
}

export async function requestJson<T>(path: string, options: RequestOptions = {}): Promise<T> {
  let response = await send(path, options)
  const canRefresh = response.status === 401 && path !== '/api/v1/auth/refresh' && path !== '/api/v1/auth/login'
  if (canRefresh) {
    try { await renewAccessToken(); response = await send(path, options) }
    catch { setAccessToken(null); authenticationLostListener?.() }
  }
  return parse<T>(response)
}

export async function downloadFile(path: string, fallbackName: string): Promise<void> {
  let response = await send(path, {})
  if (response.status === 401) {
    try { await renewAccessToken(); response = await send(path, {}) }
    catch { setAccessToken(null); authenticationLostListener?.() }
  }
  if (!response.ok) await parse<never>(response)
  const disposition = response.headers.get('content-disposition') ?? ''
  const encodedName = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1]
  const plainName = disposition.match(/filename="?([^";]+)"?/i)?.[1]
  const fileName = encodedName ? decodeURIComponent(encodedName) : plainName ?? fallbackName
  const url = URL.createObjectURL(await response.blob())
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
}

export function getJson<T>(path: string, signal?: AbortSignal, api: ApiTarget = 'core') { return requestJson<T>(path, { signal, api }) }
export function postJson<T>(path: string, body: unknown, api: ApiTarget = 'core') {
  return requestJson<T>(path, { api, method: 'POST', body: JSON.stringify(body) })
}
export function postIdempotentJson<T>(path: string, body: unknown, idempotencyKey: string) {
  return requestJson<T>(path, { method: 'POST', body: JSON.stringify(body), headers: { 'Idempotency-Key': idempotencyKey } })
}

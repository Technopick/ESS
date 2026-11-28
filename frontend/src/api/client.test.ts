import { AxiosError, AxiosHeaders } from 'axios'
import { describe, expect, it } from 'vitest'
import { errorMessage } from './client'

const axiosError = (status: number | undefined, data?: unknown) => {
  const e = new AxiosError('x', 'ERR', { headers: new AxiosHeaders() })
  if (status) e.response = { status, data, statusText: '', headers: {}, config: { headers: new AxiosHeaders() } }
  return e
}

describe('errorMessage', () => {
  it('joins validation errors', () => {
    expect(errorMessage(axiosError(400, { errors: { a: ['A is required.'], b: ['B too long.'] } }))).toBe('A is required. B too long.')
  })
  it('uses problem title', () => {
    expect(errorMessage(axiosError(401, { title: 'Invalid user name or password.' }))).toBe('Invalid user name or password.')
  })
  it('explains network failures', () => {
    expect(errorMessage(axiosError(undefined))).toMatch(/could not be reached/)
  })
  it('explains a 405 from IIS WebDAV', () => {
    expect(errorMessage(axiosError(405, '<html>405</html>'))).toMatch(/WebDAV/)
  })
  it('names the status when the answer is not problem details', () => {
    expect(errorMessage(axiosError(418, '<html></html>'))).toMatch(/HTTP 418/)
  })
  it('adds the trace reference to server errors', () => {
    expect(errorMessage(axiosError(500, { title: 'An unexpected error occurred.', traceId: '00-abc' }))).toBe('An unexpected error occurred. (reference 00-abc)')
  })
})

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
})

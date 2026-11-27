import { describe, expect, it } from 'vitest'
import { clean, statusName } from './admin'
import { Permissions, reportPermissions } from '../auth/AuthContext'

describe('admin helpers', () => {
  it('drops empty filters before they reach the API', () => {
    expect(clean({ a: '', b: null, c: undefined, d: 'x', e: 0, f: false })).toEqual({ d: 'x', e: 0, f: false })
  })
  it('names statuses in plain words', () => {
    expect(statusName('ReturnedForCorrection')).toBe('Returned')
    expect(statusName('Active')).toBe('Active')
  })
  it('opens reports for every report permission but not for plain staff', () => {
    expect(reportPermissions).toContain(Permissions.ReportsPayroll)
    expect(reportPermissions).not.toContain(Permissions.LeaveViewOwn)
  })
})

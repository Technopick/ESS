import { describe, expect, it } from 'vitest'
import { documentTypeLabel, money, statusColor } from './modules'

describe('module helpers', () => {
  it('formats Kenyan shillings with two decimals', () => {
    expect(money(1234.5)).toBe('KES 1,234.50')
    expect(money(10, 'USD')).toBe('USD 10.00')
  })
  it('colours statuses by outcome', () => {
    expect(statusColor('Approved')).toBe('success')
    expect(statusColor('Rejected')).toBe('error')
    expect(statusColor('ReturnedForCorrection')).toBe('warning')
    expect(statusColor('InApproval')).toBe('info')
    expect(statusColor('Draft')).toBe('default')
  })
  it('names every document type in plain words', () => {
    expect(documentTypeLabel('ProfileChange')).toBe('Personal details')
    expect(documentTypeLabel('HrRequest:LETTER')).toBe('HR request')
  })
})

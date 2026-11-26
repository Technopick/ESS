import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { LoginPage } from './LoginPage'

vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ me: null, login: vi.fn() }) }))

describe('LoginPage', () => {
  it('shows validation messages when submitted empty', async () => {
    render(<MemoryRouter><LoginPage /></MemoryRouter>)
    fireEvent.click(screen.getByRole('button', { name: /sign in/i }))
    await waitFor(() => expect(screen.getByText('Enter your user name or email')).toBeInTheDocument())
    expect(screen.getByText('Enter your password')).toBeInTheDocument()
  })
})

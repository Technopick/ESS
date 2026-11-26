import { Alert, Box, Button, Skeleton, Stack, Typography } from '@mui/material'
import InboxOutlinedIcon from '@mui/icons-material/InboxOutlined'
import LockOutlinedIcon from '@mui/icons-material/LockOutlined'
import { errorMessage } from '../api/client'

export function LoadingState({ rows = 3 }: { rows?: number }) {
  return (
    <Stack spacing={1} aria-busy="true" aria-label="Loading">
      {Array.from({ length: rows }).map((_, i) => (
        <Skeleton key={i} variant="rounded" height={56} />
      ))}
    </Stack>
  )
}

export function EmptyState({ title, hint }: { title: string; hint?: string }) {
  return (
    <Box className="flex flex-col items-center py-10 text-center" role="status">
      <InboxOutlinedIcon color="disabled" sx={{ fontSize: 48 }} />
      <Typography variant="subtitle1" sx={{ mt: 1 }}>
        {title}
      </Typography>
      {hint && (
        <Typography variant="body2" color="text.secondary">
          {hint}
        </Typography>
      )}
    </Box>
  )
}

export function ErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  return (
    <Alert severity="error" action={onRetry && <Button color="inherit" size="small" onClick={onRetry}>Retry</Button>}>
      {errorMessage(error)}
    </Alert>
  )
}

export function ForbiddenState() {
  return (
    <Box className="flex flex-col items-center py-16 text-center">
      <LockOutlinedIcon color="disabled" sx={{ fontSize: 48 }} />
      <Typography variant="h6" sx={{ mt: 1 }}>
        You don't have access to this page
      </Typography>
      <Typography variant="body2" color="text.secondary">
        Ask your system administrator if you think you should.
      </Typography>
    </Box>
  )
}

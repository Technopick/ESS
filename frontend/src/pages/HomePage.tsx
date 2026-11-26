import { Card, CardActionArea, CardContent, Grid2 as Grid, Typography } from '@mui/material'
import { Link } from 'react-router-dom'
import { Permissions, useAuth } from '../auth/AuthContext'
import { EmptyState } from '../components/StateViews'

export function HomePage() {
  const { me, can } = useAuth()
  return (
    <>
      <Typography variant="h1" gutterBottom>
        Welcome{me?.userName ? `, ${me.userName}` : ''}
      </Typography>
      {!me?.employeeId && (
        <Typography color="text.secondary" sx={{ mb: 3 }}>
          Your account is not linked to a NAV employee record yet, so personal modules are not shown.
        </Typography>
      )}
      <Grid container spacing={2}>
        {can(Permissions.IntegrationView) && (
          <Grid size={{ xs: 12, sm: 6, lg: 4 }}>
            <Card>
              <CardActionArea component={Link} to="/admin/integration">
                <CardContent>
                  <Typography variant="h6">NAV integration</Typography>
                  <Typography variant="body2" color="text.secondary">
                    Connection health, synchronisation and failed transactions.
                  </Typography>
                </CardContent>
              </CardActionArea>
            </Card>
          </Grid>
        )}
      </Grid>
      {!can(Permissions.IntegrationView) && <EmptyState title="Nothing to show yet" hint="Your modules will appear here." />}
    </>
  )
}

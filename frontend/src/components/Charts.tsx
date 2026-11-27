import { useId, useMemo, useRef, useState } from 'react'
import { Box, Button, Table, TableBody, TableCell, TableHead, TableRow, Typography, useTheme } from '@mui/material'

/** Single-series charts: one hue (categorical slot 1), stepped separately for light and dark surfaces. */
function useSeriesColor() {
  return useTheme().palette.mode === 'dark' ? '#338ced' : '#257de0'
}

const fmt = (n: number) => n.toLocaleString('en-KE', { maximumFractionDigits: 1 })

/** Toggles a plain table under a chart, so every figure is readable without the graphic. */
function TableToggle({ label, rows, valueLabel }: { label: string; rows: { label: string; value: number }[]; valueLabel: string }) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <Button size="small" onClick={() => setOpen(!open)} aria-expanded={open} sx={{ mt: 1 }}>{open ? 'Hide table' : 'Show table'}</Button>
      {open && (
        <Table size="small" aria-label={label}>
          <TableHead><TableRow><TableCell>{label}</TableCell><TableCell align="right">{valueLabel}</TableCell></TableRow></TableHead>
          <TableBody>{rows.map((r) => <TableRow key={r.label}><TableCell>{r.label}</TableCell><TableCell align="right">{fmt(r.value)}</TableCell></TableRow>)}</TableBody>
        </Table>
      )}
    </>
  )
}

/**
 * Horizontal bars for counts by category. Bars are 16px with a 4px rounded end and square base, the value sits at the tip
 * in text colour, and hovering a row shows its exact share. Long lists keep the top N and fold the rest into "Other".
 */
export function BarList({ title, data, valueLabel = 'Staff', max = 8 }: { title: string; data: { label: string; count: number }[]; valueLabel?: string; max?: number }) {
  const color = useSeriesColor()
  const theme = useTheme()
  const [hover, setHover] = useState<string | null>(null)
  const rows = useMemo(() => {
    const sorted = [...data].sort((a, b) => b.count - a.count)
    if (sorted.length <= max) return sorted.map((d) => ({ label: d.label, value: d.count }))
    const top = sorted.slice(0, max - 1)
    return [...top.map((d) => ({ label: d.label, value: d.count })), { label: 'Other', value: sorted.slice(max - 1).reduce((s, d) => s + d.count, 0) }]
  }, [data, max])
  const total = rows.reduce((s, r) => s + r.value, 0)
  const peak = Math.max(1, ...rows.map((r) => r.value))
  if (rows.length === 0) return <Typography variant="body2" color="text.secondary">No data for these filters.</Typography>
  return (
    <Box>
      <Typography variant="subtitle2" gutterBottom>{title}</Typography>
      <Box role="list" aria-label={title}>
        {rows.map((r) => (
          <Box key={r.label} role="listitem" aria-label={`${r.label}: ${fmt(r.value)} ${valueLabel.toLowerCase()}`}
            onMouseEnter={() => setHover(r.label)} onMouseLeave={() => setHover(null)} onFocus={() => setHover(r.label)} onBlur={() => setHover(null)} tabIndex={0}
            sx={{ display: 'grid', gridTemplateColumns: 'minmax(90px, 32%) 1fr', alignItems: 'center', gap: 1.5, py: '6px', px: 0.5, borderRadius: 1,
              bgcolor: hover === r.label ? 'action.hover' : 'transparent', outline: 'none', '&:focus-visible': { boxShadow: `0 0 0 2px ${theme.palette.primary.main}` } }}>
            <Typography variant="body2" noWrap title={r.label}>{r.label}</Typography>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, minWidth: 0 }}>
              <Box sx={{ height: 16, width: `${Math.max(2, (r.value / peak) * 85)}%`, bgcolor: color, borderRadius: '0 4px 4px 0', opacity: hover && hover !== r.label ? 0.55 : 1, transition: 'opacity 120ms' }} />
              <Typography variant="body2" sx={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
                {fmt(r.value)}{hover === r.label && total > 0 ? <Typography component="span" variant="caption" color="text.secondary"> · {Math.round((r.value / total) * 100)}%</Typography> : null}
              </Typography>
            </Box>
          </Box>
        ))}
      </Box>
      <TableToggle label={title} rows={rows} valueLabel={valueLabel} />
    </Box>
  )
}

/**
 * Line chart for a monthly series: 2px line, recessive horizontal grid, the latest value labelled at the end, and a crosshair
 * with a tooltip that follows the pointer (or arrow keys) to the nearest month.
 */
export function TrendLine({ title, data, valueLabel, height = 200 }: { title: string; data: { month: string; value: number }[]; valueLabel: string; height?: number }) {
  const color = useSeriesColor()
  const theme = useTheme()
  const id = useId()
  const ref = useRef<SVGSVGElement>(null)
  const [active, setActive] = useState<number | null>(null)
  const width = 560
  const pad = { l: 40, r: 48, t: 12, b: 26 }
  const values = data.map((d) => d.value)
  const top = Math.max(1, ...values)
  const niceTop = (() => { const p = Math.pow(10, Math.floor(Math.log10(top))); return Math.ceil(top / p) * p })()
  const x = (i: number) => pad.l + (data.length <= 1 ? 0 : (i * (width - pad.l - pad.r)) / (data.length - 1))
  const y = (v: number) => pad.t + (1 - v / niceTop) * (height - pad.t - pad.b)
  const label = (m: string) => new Date(`${m}-01T00:00:00`).toLocaleDateString('en-KE', { month: 'short' })
  const path = data.map((d, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(d.value).toFixed(1)}`).join(' ')
  const ticks = [0, niceTop / 2, niceTop]
  const ink = theme.palette.text.secondary
  const grid = theme.palette.divider

  const pick = (clientX: number) => {
    const box = ref.current?.getBoundingClientRect()
    if (!box || data.length === 0) return
    const px = ((clientX - box.left) / box.width) * width
    const i = Math.round(((px - pad.l) / (width - pad.l - pad.r)) * (data.length - 1))
    setActive(Math.min(data.length - 1, Math.max(0, i)))
  }
  if (data.length === 0) return <Typography variant="body2" color="text.secondary">No data for these filters.</Typography>
  const last = data.length - 1
  const a = active ?? null
  return (
    <Box>
      <Typography variant="subtitle2" id={`${id}-t`} gutterBottom>{title}</Typography>
      <Box sx={{ position: 'relative' }}>
        <svg ref={ref} viewBox={`0 0 ${width} ${height}`} width="100%" role="img" aria-labelledby={`${id}-t`} tabIndex={0}
          onMouseMove={(e) => pick(e.clientX)} onMouseLeave={() => setActive(null)}
          onKeyDown={(e) => {
            if (e.key === 'ArrowRight') setActive((p) => Math.min(last, (p ?? -1) + 1))
            if (e.key === 'ArrowLeft') setActive((p) => Math.max(0, (p ?? last + 1) - 1))
            if (e.key === 'Escape') setActive(null)
          }}
          onBlur={() => setActive(null)} style={{ display: 'block', outline: 'none', touchAction: 'pan-y' }}>
          {ticks.map((t) => (
            <g key={t}>
              <line x1={pad.l} x2={width - pad.r} y1={y(t)} y2={y(t)} stroke={grid} strokeWidth={1} />
              <text x={pad.l - 6} y={y(t)} dy="0.32em" textAnchor="end" fontSize={11} fill={ink}>{fmt(t)}</text>
            </g>
          ))}
          {data.map((d, i) => (i % 2 === last % 2 ? <text key={d.month} x={x(i)} y={height - 6} textAnchor="middle" fontSize={11} fill={ink}>{label(d.month)}</text> : null))}
          <path d={path} fill="none" stroke={color} strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
          <circle cx={x(last)} cy={y(data[last].value)} r={4} fill={color} stroke={theme.palette.background.paper} strokeWidth={2} />
          <text x={x(last) + 8} y={y(data[last].value)} dy="0.32em" fontSize={12} fill={theme.palette.text.primary}>{fmt(data[last].value)}</text>
          {a !== null && (
            <g pointerEvents="none">
              <line x1={x(a)} x2={x(a)} y1={pad.t} y2={height - pad.b} stroke={ink} strokeWidth={1} strokeDasharray="3 3" />
              <circle cx={x(a)} cy={y(data[a].value)} r={5} fill={color} stroke={theme.palette.background.paper} strokeWidth={2} />
            </g>
          )}
        </svg>
        {a !== null && (
          <Box role="status" sx={{ position: 'absolute', top: 0, left: `${(x(a) / width) * 100}%`, transform: x(a) > width * 0.7 ? 'translateX(calc(-100% - 12px))' : 'translateX(12px)',
            bgcolor: 'background.paper', border: 1, borderColor: 'divider', borderRadius: 1, px: 1, py: 0.5, boxShadow: 2, pointerEvents: 'none', whiteSpace: 'nowrap' }}>
            <Typography variant="caption" color="text.secondary" display="block">
              {new Date(`${data[a].month}-01T00:00:00`).toLocaleDateString('en-KE', { month: 'long', year: 'numeric' })}
            </Typography>
            <Typography variant="body2" fontWeight={600}>{fmt(data[a].value)} {valueLabel.toLowerCase()}</Typography>
          </Box>
        )}
      </Box>
      <TableToggle label={title} rows={data.map((d) => ({ label: label(d.month) + ' ' + d.month.slice(0, 4), value: d.value }))} valueLabel={valueLabel} />
    </Box>
  )
}

/** Headline number with a short label: for single figures a chart would only decorate. */
export function StatTile({ label, value, hint }: { label: string; value: string | number; hint?: string }) {
  return (
    <Box sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 2, height: '100%' }}>
      <Typography variant="body2" color="text.secondary">{label}</Typography>
      <Typography variant="h4" component="p" sx={{ fontVariantNumeric: 'tabular-nums', mt: 0.5 }}>{typeof value === 'number' ? fmt(value) : value}</Typography>
      {hint && <Typography variant="caption" color="text.secondary">{hint}</Typography>}
    </Box>
  )
}

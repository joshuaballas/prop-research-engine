import CheckCircleOutlineRoundedIcon from '@mui/icons-material/CheckCircleOutlineRounded';
import KeyboardArrowDownRoundedIcon from '@mui/icons-material/KeyboardArrowDownRounded';
import KeyboardArrowUpRoundedIcon from '@mui/icons-material/KeyboardArrowUpRounded';
import { useState, type ReactNode } from 'react';
import { Box, Button, Collapse, Grid, IconButton, Stack, Typography } from '@mui/material';
import { AppCard } from '../../components/ui/AppCard';
import { MetricCard } from '../../components/ui/MetricCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import { palette } from '../../theme/palette';

const trackedItems = [
  'Submitted entries',
  'Leg-by-leg results',
  'Estimated vs actual payout',
  'Provider and entry type',
  'Win/loss record',
  'Net profit/loss'
];

const workflowSteps = [
  { title: 'Build an entry', body: 'Use the drawer to pick compatible props.' },
  { title: 'Submit manually', body: 'Place the entry in the provider app.' },
  { title: 'Track results', body: "Save it here and we'll grade each leg after results import." }
];

const exampleEntries = [
  {
    id: 'prizepicks-power-example',
    date: 'Example',
    provider: 'PrizePicks',
    entryType: 'Power',
    legs: 3,
    stake: '$25',
    estimatedPayout: '$150',
    status: 'Pending',
    result: '-',
    net: '-',
    legRows: [
      { player: 'Chase Burns', pick: 'Under 7', prop: 'Pitcher Strikeouts', finalStat: '-', result: 'Pending' },
      { player: 'Endy Rodriguez', pick: 'Under 1.5', prop: 'Total Bases', finalStat: '-', result: 'Pending' },
      { player: 'Nick Martinez', pick: 'Under 4', prop: 'Pitcher Strikeouts', finalStat: '-', result: 'Pending' }
    ]
  },
  {
    id: 'underdog-pickem-example',
    date: 'Example',
    provider: 'Underdog',
    entryType: "Pick'em",
    legs: 5,
    stake: '$20',
    estimatedPayout: '$200',
    status: 'Lost',
    result: '3/5 legs',
    net: '-$20',
    legRows: [
      { player: 'Example player', pick: 'Over 0.5', prop: 'Hits', finalStat: '1', result: 'Won' },
      { player: 'Example player', pick: 'Under 1.5', prop: 'Total Bases', finalStat: '3', result: 'Lost' },
      { player: 'Example player', pick: 'Under 5.5', prop: 'Pitcher Strikeouts', finalStat: '5', result: 'Won' }
    ]
  },
  {
    id: 'betr-power-example',
    date: 'Example',
    provider: 'Betr Picks',
    entryType: 'Power',
    legs: 2,
    stake: '$10',
    estimatedPayout: '$30',
    status: 'Won',
    result: '2/2 legs',
    net: '+$20',
    legRows: [
      { player: 'Example player', pick: 'Under 6.5', prop: 'Pitcher Strikeouts', finalStat: '4', result: 'Won' },
      { player: 'Example player', pick: 'Over 1.5', prop: 'Total Bases', finalStat: '2', result: 'Won' }
    ]
  }
];

type ExampleEntry = (typeof exampleEntries)[number];
type ExampleLeg = ExampleEntry['legRows'][number];

export function EntryTrackerPage() {
  const builder = useEntryBuilder();

  return (
    <Stack spacing={3} sx={{ pb: { xs: 'calc(76px + env(safe-area-inset-bottom))', lg: 0 } }}>
      <PageHeader title="Entry Tracker" description="Track entries you submit and review results after games finish." />

      <Grid container spacing={1.5} alignItems="stretch">
        <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
          <MetricCard label="Tracked Entries" value="Coming later" helper="Saved entries" />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
          <MetricCard label="Open Entries" value="-" helper="Waiting for results" />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
          <MetricCard label="Record" value="-" helper="Settled entries" />
        </Grid>
        <Grid size={{ xs: 12, sm: 6, lg: 3 }}>
          <MetricCard label="Net Result" value="-" helper="Tracked profit/loss" />
        </Grid>
      </Grid>

      <Grid container spacing={2} alignItems="flex-start">
        <Grid size={{ xs: 12, lg: 8 }}>
          <Stack spacing={2}>
            <EntryTrackerEmptyState onOpenEntry={builder.openEntryBuilder} />
            <EntryHistoryPreview />
          </Stack>
        </Grid>

        <Grid size={{ xs: 12, lg: 4 }}>
          <Stack spacing={2}>
            <TrackerListCard title="What this page will track" items={trackedItems} />
            <HowItWorksCard />
          </Stack>
        </Grid>
      </Grid>
    </Stack>
  );
}

function EntryTrackerEmptyState({ onOpenEntry }: { onOpenEntry: () => void }) {
  return (
    <AppCard>
      <Stack spacing={1.5} alignItems="flex-start">
        <Stack spacing={0.6}>
          <Typography variant="h4">Entry tracking is coming later.</Typography>
          <Typography color="text.secondary">
            Build an entry in the drawer, submit it manually in the provider app, then track it here once tracking is available.
          </Typography>
        </Stack>
        <Button
          variant="contained"
          onClick={onOpenEntry}
          sx={{ boxShadow: 'none', '&:hover, &:focus, &:active': { boxShadow: 'none' } }}
        >
          Open current entry
        </Button>
        <Typography variant="body2" color="text.secondary">
          This does not place entries for you. You still submit entries manually with the provider.
        </Typography>
      </Stack>
    </AppCard>
  );
}

function EntryHistoryPreview() {
  const [expandedId, setExpandedId] = useState(exampleEntries[0].id);
  const toggleExpanded = (entryId: string) => setExpandedId((current) => (current === entryId ? '' : entryId));

  return (
    <AppCard>
      <Stack spacing={1.5}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1.5}>
          <Stack spacing={0.4}>
            <Stack direction="row" gap={1} alignItems="center" flexWrap="wrap" useFlexGap>
              <Typography variant="h5">Entry History Preview</Typography>
              <ExampleBadge />
            </Stack>
            <Typography variant="body2" color="text.secondary">
              Example rows show how tracked entries will be listed after tracking is available.
            </Typography>
          </Stack>
        </Stack>

        <FilterPreview />
        <DesktopEntryHistoryTable expandedId={expandedId} onToggle={toggleExpanded} />
        <MobileEntryHistoryCards expandedId={expandedId} onToggle={toggleExpanded} />

        <Typography variant="body2" color="text.secondary">
          Single-pick sportsbook tracking can use the same history view when sportsbook entries are supported.
        </Typography>
      </Stack>
    </AppCard>
  );
}

function FilterPreview() {
  return (
    <Stack direction="row" gap={1} flexWrap="wrap" useFlexGap alignItems="center">
      {['Status', 'Provider', 'Date range'].map((label) => (
        <Box
          key={label}
          sx={{
            border: `1px solid ${palette.border}`,
            borderRadius: 999,
            px: 1.15,
            py: 0.55,
            color: 'text.secondary',
            bgcolor: 'rgba(7,11,20,.42)',
            fontSize: 13,
            fontWeight: 900
          }}
        >
          {label}
        </Box>
      ))}
      <Typography variant="caption" color="text.secondary" fontWeight={900}>
        Filters coming later
      </Typography>
    </Stack>
  );
}

function DesktopEntryHistoryTable({ expandedId, onToggle }: { expandedId: string; onToggle: (entryId: string) => void }) {
  return (
    <Box
      sx={{
        display: { xs: 'none', md: 'block' },
        border: `1px solid ${palette.border}`,
        borderRadius: 2,
        overflow: 'hidden',
        bgcolor: '#081321'
      }}
    >
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: '0.55fr 1fr 0.8fr 0.45fr 0.55fr 0.8fr 0.7fr 0.7fr 0.55fr 38px',
          borderBottom: `1px solid ${palette.border}`,
          bgcolor: 'rgba(15,23,42,.58)'
        }}
      >
        {['Date', 'Provider', 'Entry Type', 'Legs', 'Stake', 'Est. Payout', 'Status', 'Result', 'Net', ''].map((header) => (
          <TableCell key={header} header>
            {header}
          </TableCell>
        ))}
      </Box>
      {exampleEntries.map((entry) => (
        <Box key={entry.id}>
          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: '0.55fr 1fr 0.8fr 0.45fr 0.55fr 0.8fr 0.7fr 0.7fr 0.55fr 38px',
              borderBottom: expandedId === entry.id ? `1px solid ${palette.border}` : '1px solid rgba(148,163,184,.1)'
            }}
          >
            <TableCell>{entry.date}</TableCell>
            <TableCell strong>{entry.provider}</TableCell>
            <TableCell>{entry.entryType}</TableCell>
            <TableCell>{entry.legs}</TableCell>
            <TableCell>{entry.stake}</TableCell>
            <TableCell>{entry.estimatedPayout}</TableCell>
            <TableCell tone={statusTone(entry.status)} strong>{entry.status}</TableCell>
            <TableCell>{entry.result}</TableCell>
            <TableCell strong tone={entry.net.startsWith('+') ? 'success' : entry.net.startsWith('-') ? 'error' : 'default'}>{entry.net}</TableCell>
            <TableCell>
              <IconButton
                size="small"
                onClick={() => onToggle(entry.id)}
                aria-label={expandedId === entry.id ? 'Collapse example entry' : 'Expand example entry'}
                sx={{ boxShadow: 'none' }}
              >
                {expandedId === entry.id ? <KeyboardArrowUpRoundedIcon fontSize="small" /> : <KeyboardArrowDownRoundedIcon fontSize="small" />}
              </IconButton>
            </TableCell>
          </Box>
          <Collapse in={expandedId === entry.id} unmountOnExit>
            <LegDetailTable legs={entry.legRows} />
          </Collapse>
        </Box>
      ))}
    </Box>
  );
}

function MobileEntryHistoryCards({ expandedId, onToggle }: { expandedId: string; onToggle: (entryId: string) => void }) {
  return (
    <Stack spacing={1} sx={{ display: { xs: 'flex', md: 'none' } }}>
      {exampleEntries.map((entry) => (
        <Box key={entry.id} sx={{ border: `1px solid ${palette.border}`, borderRadius: 2, bgcolor: '#081321', overflow: 'hidden' }}>
          <Stack spacing={1} sx={{ p: 1.15 }}>
            <Stack direction="row" justifyContent="space-between" gap={1} alignItems="flex-start">
              <Box minWidth={0}>
                <Typography fontWeight={950}>{entry.provider}</Typography>
                <Typography variant="body2" color="text.secondary">
                  {entry.entryType} · {entry.legs} legs
                </Typography>
              </Box>
              <StatusText tone={entry.status}>{entry.status}</StatusText>
            </Stack>
            <Grid container spacing={1}>
              <MobileMetric label="Stake" value={entry.stake} />
              <MobileMetric label="Est. payout" value={entry.estimatedPayout} />
              <MobileMetric label="Result" value={entry.result} />
              <MobileMetric label="Net" value={entry.net} tone={entry.net.startsWith('+') ? 'success' : entry.net.startsWith('-') ? 'error' : 'default'} />
            </Grid>
            <Button
              size="small"
              variant="text"
              onClick={() => onToggle(entry.id)}
              endIcon={expandedId === entry.id ? <KeyboardArrowUpRoundedIcon /> : <KeyboardArrowDownRoundedIcon />}
              sx={{
                alignSelf: 'flex-start',
                minWidth: 'auto',
                p: 0,
                boxShadow: 'none',
                '&:hover, &:focus, &:active': { boxShadow: 'none', bgcolor: 'transparent' }
              }}
            >
              {expandedId === entry.id ? 'Hide legs' : 'Show legs'}
            </Button>
          </Stack>
          <Collapse in={expandedId === entry.id} unmountOnExit>
            <LegDetailList legs={entry.legRows} />
          </Collapse>
        </Box>
      ))}
    </Stack>
  );
}

function LegDetailTable({ legs }: { legs: ExampleLeg[] }) {
  return (
    <Box sx={{ p: 1.1, bgcolor: 'rgba(7,11,20,.55)' }}>
      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: '1.15fr 0.8fr 1fr 0.65fr 0.7fr',
          border: `1px solid ${palette.border}`,
          borderRadius: 2,
          overflow: 'hidden'
        }}
      >
        {['Player', 'Pick', 'Prop', 'Final stat', 'Result'].map((header) => (
          <TableCell key={header} header>
            {header}
          </TableCell>
        ))}
        {legs.map((leg) => (
          <Box key={`${leg.player}-${leg.pick}-${leg.prop}`} sx={{ display: 'contents' }}>
            <TableCell strong>{leg.player}</TableCell>
            <TableCell>{leg.pick}</TableCell>
            <TableCell>{leg.prop}</TableCell>
            <TableCell>{leg.finalStat}</TableCell>
            <TableCell tone={statusTone(leg.result)} strong>{leg.result}</TableCell>
          </Box>
        ))}
      </Box>
    </Box>
  );
}

function LegDetailList({ legs }: { legs: ExampleLeg[] }) {
  return (
    <Stack spacing={0.75} sx={{ p: 1.1, pt: 0, bgcolor: 'rgba(7,11,20,.55)' }}>
      {legs.map((leg) => (
        <Box key={`${leg.player}-${leg.pick}-${leg.prop}`} sx={{ borderTop: `1px solid ${palette.border}`, pt: 0.8 }}>
          <Stack direction="row" justifyContent="space-between" gap={1}>
            <Box minWidth={0}>
              <Typography variant="body2" fontWeight={950}>{leg.player}</Typography>
              <Typography variant="caption" color="text.secondary">
                {leg.pick} · {leg.prop} · Final stat {leg.finalStat}
              </Typography>
            </Box>
            <StatusText tone={leg.result}>{leg.result}</StatusText>
          </Stack>
        </Box>
      ))}
    </Stack>
  );
}

function MobileMetric({ label, value, tone = 'default' }: { label: string; value: string; tone?: 'default' | 'success' | 'error' }) {
  return (
    <Grid size={6}>
      <Box>
        <Typography variant="caption" color="text.secondary" fontWeight={900}>
          {label}
        </Typography>
        <Typography fontWeight={950} color={tone === 'success' ? 'success.main' : tone === 'error' ? 'error.main' : 'text.primary'}>
          {value}
        </Typography>
      </Box>
    </Grid>
  );
}

function TrackerListCard({ title, items }: { title: string; items: string[] }) {
  return (
    <AppCard>
      <Stack spacing={1.4}>
        <Typography variant="h5">{title}</Typography>
        <Stack spacing={1}>
          {items.map((item) => (
            <Stack key={item} direction="row" spacing={1} alignItems="center">
              <IconDot />
              <Typography fontWeight={900}>{item}</Typography>
            </Stack>
          ))}
        </Stack>
      </Stack>
    </AppCard>
  );
}

function HowItWorksCard() {
  return (
    <AppCard>
      <Stack spacing={1.4}>
        <Typography variant="h5">How it will work</Typography>
        <Stack spacing={1.1}>
          {workflowSteps.map((step, index) => (
            <Stack key={step.title} direction="row" spacing={1.1} alignItems="flex-start">
              <Box
                sx={{
                  width: 28,
                  height: 28,
                  borderRadius: '50%',
                  display: 'grid',
                  placeItems: 'center',
                  flexShrink: 0,
                  color: 'primary.main',
                  border: `1px solid ${palette.border}`,
                  bgcolor: 'rgba(34,211,238,.08)',
                  fontWeight: 950,
                  fontSize: 13
                }}
              >
                {index + 1}
              </Box>
              <Box>
                <Typography fontWeight={950}>{step.title}</Typography>
                <Typography variant="body2" color="text.secondary">
                  {step.body}
                </Typography>
              </Box>
            </Stack>
          ))}
        </Stack>
      </Stack>
    </AppCard>
  );
}

function ExampleBadge() {
  return (
    <Box
      sx={{
        border: '1px solid rgba(148,163,184,.22)',
        borderRadius: 999,
        px: 0.8,
        py: 0.25,
        color: 'text.secondary',
        fontSize: 11,
        fontWeight: 950,
        textTransform: 'uppercase',
        letterSpacing: 0
      }}
    >
      Example
    </Box>
  );
}

function StatusText({ children, tone }: { children: string; tone?: string }) {
  const color = statusColor(tone);
  return (
    <Typography variant="body2" color={color} fontWeight={950} sx={{ whiteSpace: 'nowrap' }}>
      {children}
    </Typography>
  );
}

function statusTone(value?: string): 'default' | 'success' | 'error' {
  return value === 'Won' ? 'success' : value === 'Lost' ? 'error' : 'default';
}

function statusColor(value?: string) {
  return value === 'Won' ? 'success.main' : value === 'Lost' ? 'error.main' : 'text.secondary';
}

function TableCell({
  children,
  header = false,
  strong = false,
  tone = 'default'
}: {
  children: ReactNode;
  header?: boolean;
  strong?: boolean;
  tone?: 'default' | 'success' | 'error';
}) {
  return (
    <Box
      sx={{
        minWidth: 0,
        px: 1,
        py: header ? 0.8 : 0.9,
        borderRight: `1px solid ${palette.border}`,
        borderBottom: header ? 0 : '1px solid rgba(148,163,184,.1)'
      }}
    >
      <Typography
        variant={header ? 'caption' : 'body2'}
        color={tone === 'success' ? 'success.main' : tone === 'error' ? 'error.main' : header ? 'text.secondary' : 'text.primary'}
        fontWeight={header || strong ? 950 : 700}
        sx={{ overflowWrap: 'anywhere' }}
      >
        {children}
      </Typography>
    </Box>
  );
}

function IconDot() {
  return (
    <Box
      sx={{
        width: 26,
        height: 26,
        borderRadius: '50%',
        display: 'grid',
        placeItems: 'center',
        color: 'primary.main',
        border: `1px solid ${palette.border}`,
        bgcolor: 'rgba(34,211,238,.08)',
        flexShrink: 0
      }}
    >
      <CheckCircleOutlineRoundedIcon sx={{ fontSize: 16 }} />
    </Box>
  );
}

import AccountTreeOutlinedIcon from '@mui/icons-material/AccountTreeOutlined';
import AutoGraphOutlinedIcon from '@mui/icons-material/AutoGraphOutlined';
import BoltOutlinedIcon from '@mui/icons-material/BoltOutlined';
import FactCheckOutlinedIcon from '@mui/icons-material/FactCheckOutlined';
import GridViewOutlinedIcon from '@mui/icons-material/GridViewOutlined';
import InsightsOutlinedIcon from '@mui/icons-material/InsightsOutlined';
import LocalFireDepartmentOutlinedIcon from '@mui/icons-material/LocalFireDepartmentOutlined';
import SellOutlinedIcon from '@mui/icons-material/SellOutlined';
import ShieldOutlinedIcon from '@mui/icons-material/ShieldOutlined';
import StarsOutlinedIcon from '@mui/icons-material/StarsOutlined';
import StackedLineChartOutlinedIcon from '@mui/icons-material/StackedLineChartOutlined';
import SyncOutlinedIcon from '@mui/icons-material/SyncOutlined';
import TheaterComedyOutlinedIcon from '@mui/icons-material/TheaterComedyOutlined';
import WarningAmberOutlinedIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Button, Container, Divider, Grid, LinearProgress, Stack, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { LineShoppingTable } from '../../components/markets/LineShoppingTable';
import { TopPickCard } from '../../components/markets/TopPickCard';
import { ProviderLogo } from '../../components/providers/ProviderLogo';
import { AppCard } from '../../components/ui/AppCard';
import { MetadataText } from '../../components/ui/MetadataText';
import { SectionHeader } from '../../components/ui/SectionHeader';
import { StatusPill } from '../../components/ui/StatusPill';
import { mockMarkets } from '../../data/mockMarkets';
import { mockTopPicks } from '../../data/mockTopPicks';
import { palette } from '../../theme/palette';

const backgroundTerms = [
  'PrizePicks',
  'Underdog',
  'DraftKings Pick6',
  'Betr Picks',
  'Demon',
  'Goblin',
  'Discounted Line',
  'Payout Boost',
  'MLB',
  'NBA',
  'MMA',
  'Golf',
  'Tennis',
  'Total Bases',
  'Pitcher Ks',
  'Rebounds',
  'Birdies',
  'Significant Strikes'
];

const dfsPlatforms = [
  { name: 'PrizePicks', mark: 'PP' },
  { name: 'Underdog', mark: 'UD' },
  { name: 'DraftKings Pick6', mark: 'P6' },
  { name: 'Betr Picks', mark: 'BP' }
];
const sportsbookContext = [
  { name: 'DraftKings', mark: 'DK' },
  { name: 'FanDuel', mark: 'FD' },
  { name: 'BetMGM', mark: 'MGM' },
  { name: 'ESPN Bet', mark: 'ESPN' },
  { name: 'Caesars', mark: 'CZR' },
  { name: 'BetRivers', mark: 'BR' },
  { name: 'Fanatics', mark: 'FAN' }
];
const sports = ['MLB', 'NBA', 'MMA', 'Golf', 'Tennis'];
const modifiers = [
  { label: 'Demon', icon: <LocalFireDepartmentOutlinedIcon />, color: '#FB6B47', detail: 'Higher projection, adjusted payout' },
  { label: 'Goblin', icon: <TheaterComedyOutlinedIcon />, color: '#34D399', detail: 'Lower projection, adjusted payout' },
  { label: 'Discounted Line', icon: <SellOutlinedIcon />, color: '#FBBF24', detail: 'Reduced projection context' },
  { label: 'Payout Boost', icon: <BoltOutlinedIcon />, color: '#22D3EE', detail: 'Boosted estimated payout' },
  { label: 'Bonus Lineup', icon: <StarsOutlinedIcon />, color: '#A78BFA', detail: 'Provider promo context' }
];
const statuses = [
  { label: 'Fresh', tone: palette.primary, detail: 'Recent market scan' },
  { label: 'Stale', tone: palette.warning, detail: 'Review before action' },
  { label: 'Limited coverage', tone: palette.secondary, detail: 'Partial market coverage' },
  { label: 'No active opportunities', tone: palette.textMuted, detail: 'Nothing meets filters' },
  { label: 'Low sample size', tone: palette.danger, detail: 'Research caution' }
];

export function HomePage() {
  const featuredPick = mockTopPicks[0];

  return (
    <Box sx={{ position: 'relative', overflow: 'hidden' }}>
      <ScannerBackground />

      <Box component="section" sx={{ position: 'relative', pt: { xs: 7, md: 10 }, pb: { xs: 6, md: 10 } }}>
        <Container maxWidth="xl">
          <Grid container spacing={{ xs: 5, md: 7 }} alignItems="center">
            <Grid size={{ xs: 12, lg: 5 }}>
              <Stack spacing={3}>
                <Typography variant="h1" fontSize={{ xs: 38, sm: 56, md: 72 }} maxWidth={760}>
                  DFS research that sees the market before you build.
                </Typography>
                <Typography color="text.secondary" fontSize={{ xs: 17, md: 19 }} lineHeight={1.75} maxWidth={680}>
                  Compare DFS projections, see sportsbook context, spot model edges, and avoid mixing incompatible providers before you open the provider app.
                </Typography>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
                  <Button component={RouterLink} to="/pricing" variant="contained" size="large">
                    View Plans
                  </Button>
                  <Button component={RouterLink} to="/#how-it-works" variant="outlined" size="large">
                    See How It Works
                  </Button>
                </Stack>
                <Typography variant="caption" color="text.secondary" maxWidth={640}>
                  Research tools only. Confirm projections, final payout, and entry availability in the provider app before submitting.
                </Typography>
              </Stack>
            </Grid>

            <Grid size={{ xs: 12, lg: 7 }}>
              <HeroProductPreview>
                <TopPickCard market={featuredPick} hideActions />
              </HeroProductPreview>
            </Grid>
          </Grid>
        </Container>
      </Box>

      <LandingSection id="how-it-works" eyebrow="How it works" title="Compare projections, rank candidates, then build by provider.">
        <WorkflowTimeline />
      </LandingSection>

      <LandingSection eyebrow="Coverage" title="Actionable DFS platforms with sportsbook reference lines.">
        <CoverageBoard />
      </LandingSection>

      <LandingSection eyebrow="Entry intelligence" title="See Demons, Goblins, discounts, and boosts in context.">
        <ModifierIntelligence />
      </LandingSection>

      <LandingSection eyebrow="Freshness and trust" title="Clear market-state labels before a row becomes a candidate.">
        <FreshnessTrustPanel />
      </LandingSection>

      <LandingSection eyebrow="Line shopping preview" title="Scan the best DFS line beside sportsbook reference lines.">
        <AppCard sx={{ overflow: 'hidden', position: 'relative' }}>
          <Stack spacing={2.5}>
            <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" gap={2}>
              <Stack spacing={0.5}>
                <Typography variant="h5">Market comparison board</Typography>
                <Typography color="text.secondary">
                  Candidate rows show the DFS provider, provider entry group, sportsbook context, estimated edge, and caution labels in one pass.
                </Typography>
              </Stack>
              <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                <StatusPill label="Fresh" />
                <StatusPill label="DFS" />
                <StatusPill label="Limited history" />
              </Stack>
            </Stack>
            <LineShoppingTable markets={mockMarkets.slice(0, 4)} showActions={false} />
          </Stack>
        </AppCard>
      </LandingSection>

      <Container maxWidth="xl" sx={{ pb: { xs: 6, md: 10 } }}>
        <AppCard
          sx={{
            position: 'relative',
            overflow: 'hidden',
            borderRadius: 4,
            maxWidth: 1120,
            mx: 'auto',
            borderColor: 'rgba(125,211,252,.24)',
            background: '#08111F !important',
            '&:before': {
              display: 'none'
            },
            '&:after': {
              display: 'none'
            }
          }}
        >
          <Grid container spacing={2.5} alignItems="center" sx={{ position: 'relative', p: { xs: 0.5, md: 1 } }}>
            <Grid size={{ xs: 12, md: 8 }}>
              <Stack spacing={1.35}>
                <Typography variant="h3" fontSize={{ xs: 25, sm: 28, md: 32 }}>
                  Ready to compare your next slate?
                </Typography>
                <Typography color="rgba(226,232,240,.82)" maxWidth={680}>
                  Start with provider-safe entries, reference lines, and modifier-aware payout context.
                </Typography>
                <Stack direction="row" spacing={1.3} flexWrap="wrap" useFlexGap>
                  {['Provider-safe entries', 'Reference lines', 'Modifier-aware payouts', 'Freshness labels'].map((label) => (
                    <MetadataText key={label}>{label}</MetadataText>
                  ))}
                </Stack>
              </Stack>
            </Grid>
            <Grid size={{ xs: 12, md: 4 }}>
              <Stack
                direction={{ xs: 'column', sm: 'row', md: 'column' }}
                spacing={1.25}
                justifyContent={{ md: 'flex-end' }}
              >
                <Button component={RouterLink} to="/pricing" variant="contained" size="large">
                  View Plans
                </Button>
                <Button component={RouterLink} to="/sign-in" variant="outlined" size="large">
                  Sign In
                </Button>
              </Stack>
            </Grid>
          </Grid>
        </AppCard>
      </Container>
    </Box>
  );
}

function ScannerBackground() {
  return (
    <Box
      aria-hidden
      sx={{
        position: 'absolute',
        inset: 0,
        overflow: 'hidden',
        pointerEvents: 'none',
        backgroundColor: '#070B14'
      }}
    >
      <Stack
        direction="row"
        gap={1}
        flexWrap="wrap"
        sx={{
          display: { xs: 'none', sm: 'flex' },
          position: 'absolute',
          top: { xs: 24, md: 34 },
          left: '50%',
          width: { xs: '120%', md: 980 },
          transform: 'translateX(-48%) rotate(-4deg)',
          opacity: 0.28
        }}
      >
        {backgroundTerms.map((term) => (
          <Box
            key={term}
            sx={{
              px: 0.5,
              py: 0.2,
              color: 'rgba(226,232,240,.76)',
              fontSize: 12,
              fontWeight: 850
            }}
          >
            {term}
          </Box>
        ))}
      </Stack>
    </Box>
  );
}

function HeroProductPreview({ children }: { children: ReactNode }) {
  return (
    <>
      <Box
        sx={{
          display: { xs: 'block', sm: 'none' },
          borderRadius: 4,
          p: 2,
          pt: 2.25,
          border: '1px solid rgba(125,211,252,.22)',
          backgroundColor: palette.cardSurface,
          boxShadow: '0 18px 58px rgba(0,0,0,.44)',
          overflow: 'hidden'
        }}
      >
        <Stack spacing={1.5}>
          <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1.25} flexWrap="wrap" useFlexGap sx={{ px: 0.25 }}>
            <MetadataText tone="fresh">Markets updated 3m ago</MetadataText>
            <Typography variant="caption" color="text.secondary" fontWeight={850}>
              15 active candidates
            </Typography>
          </Stack>
          {children}
          <LineStripPreview />
          <EntrySummaryPreview compact />
        </Stack>
      </Box>

      <Box
        sx={{
          display: { xs: 'none', sm: 'block' },
          position: 'relative',
          borderRadius: 4,
          p: { sm: 2, md: 2.2 },
          border: '1px solid rgba(125,211,252,.22)',
          backgroundColor: palette.cardSurface,
          boxShadow: '0 24px 90px rgba(0,0,0,.48)',
          overflow: 'hidden'
        }}
      >
        <Stack spacing={1.5} sx={{ position: 'relative', zIndex: 1 }}>
          <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
            <MetadataText tone="fresh">Markets updated 3m ago</MetadataText>
            <Typography variant="caption" color="text.secondary" fontWeight={850}>
              15 active candidates
            </Typography>
          </Stack>

          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: {
                sm: '96px minmax(0, 1fr)',
                md: '96px minmax(340px, 1fr) 230px',
                lg: '104px minmax(380px, 1fr) 246px'
              },
              gap: { sm: 1.25, md: 1.5 },
              alignItems: 'start'
            }}
          >
            <Box sx={{ gridColumn: '1', minWidth: 0 }}>
              <SportRailPreview />
            </Box>

            <Box
              sx={{
                gridColumn: '2',
                minWidth: 0
              }}
            >
              {children}
            </Box>

            <Box
              sx={{
                gridColumn: { sm: '2', md: '3' },
                gridRow: { sm: '2', md: '1' },
                minWidth: 0
              }}
            >
              <EntrySummaryPreview />
            </Box>
          </Box>

          <LineStripPreview />
        </Stack>
      </Box>
    </>
  );
}

function SportRailPreview() {
  return (
    <AppCard sx={{ p: 0, background: palette.cardSurface }}>
      <Stack spacing={0.85}>
        <Typography variant="caption" color="text.secondary" fontWeight={900}>
          SPORTS
        </Typography>
        {sports.map((sport, index) => (
          <Box
            key={sport}
            sx={{
              px: 1,
              py: 0.75,
              borderRadius: 1.5,
              bgcolor: index === 0 ? 'rgba(34,211,238,.14)' : 'rgba(148,163,184,.08)',
              color: index === 0 ? palette.primary : palette.textMuted,
              border: `1px solid ${index === 0 ? 'rgba(34,211,238,.28)' : 'rgba(148,163,184,.12)'}`,
              fontSize: 12,
              fontWeight: 950,
              textAlign: 'center'
            }}
          >
            {sport}
          </Box>
        ))}
      </Stack>
    </AppCard>
  );
}

function EntrySummaryPreview({ compact = false }: { compact?: boolean }) {
  return (
    <AppCard sx={{ background: palette.cardSurface }}>
      <Stack spacing={compact ? 1 : 1.35}>
        <Stack direction="row" justifyContent="space-between" alignItems="center">
          <Typography variant="caption" color="text.secondary" fontWeight={900}>
            ENTRY SCOPE
          </Typography>
          <StatusPill label="DFS" />
        </Stack>
        <Typography variant="h6" lineHeight={1}>
          PrizePicks
        </Typography>
        <Stack spacing={compact ? 0.55 : 0.7}>
          <MiniBar label="Compatible legs" value={62} />
          <MiniBar label="Fresh candidates" value={78} />
          <MiniBar label="Modifier checks" value={44} />
        </Stack>
        <Typography variant="caption" color="text.secondary">
          Provider-scoped entries with estimated payout context.
        </Typography>
      </Stack>
    </AppCard>
  );
}

function LineStripPreview() {
  const strip = mockMarkets.slice(0, 3);
  return (
    <AppCard sx={{ background: palette.cardSurface }}>
      <Stack spacing={1.25}>
        <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
          <Typography variant="caption" color="text.secondary" fontWeight={900}>
            LINE SHOPPING STRIP
          </Typography>
          <MetadataText>REFERENCE LINES</MetadataText>
        </Stack>
        <Stack spacing={0.85}>
          {strip.map((row) => (
            <Grid key={row.id} container alignItems="center" spacing={1}>
              <Grid size={{ xs: 5 }}>
                <Typography variant="body2" color="text.secondary" fontWeight={760} noWrap>
                  {row.player}
                </Typography>
              </Grid>
              <Grid size={{ xs: 3 }}>
                <Typography variant="body2" color="text.secondary" fontWeight={760} noWrap>
                  {row.side} {row.line}
                </Typography>
              </Grid>
              <Grid size={{ xs: 4 }}>
                <Typography variant="body2" color={row.estimatedEv > 0 ? 'success.main' : 'text.secondary'} textAlign="right" fontWeight={950}>
                  {row.estimatedEv > 0 ? '+' : ''}
                  {(row.estimatedEv * 100).toFixed(1)}% EV
                </Typography>
              </Grid>
            </Grid>
          ))}
        </Stack>
      </Stack>
    </AppCard>
  );
}

function WorkflowTimeline() {
  const steps = [
    {
      icon: <StackedLineChartOutlinedIcon />,
      title: 'Compare lines',
      body: 'Scan DFS projections beside sportsbook reference lines, line movement shape, freshness, and provider coverage.'
    },
    {
      icon: <AutoGraphOutlinedIcon />,
      title: 'Rank candidates',
      body: 'Promote rows with positive model edge, active market status, provider compatibility, and enough history to review.'
    },
    {
      icon: <AccountTreeOutlinedIcon />,
      title: 'Build safely',
      body: 'Keep candidate legs grouped by provider so PrizePicks, Underdog, Pick6, and Betr Picks stay in their own entry paths.'
    }
  ];

  return (
    <Box
      sx={{
        position: 'relative'
      }}
    >
      <Grid container spacing={2.5}>
        {steps.map((step, index) => (
          <Grid key={step.title} size={{ xs: 12, md: 4 }}>
            <AppCard sx={{ height: '100%', background: palette.cardSurface }}>
              <Stack spacing={2}>
                <Stack direction="row" alignItems="center" spacing={1.4}>
                  <Box
                    sx={{
                      width: 54,
                      height: 54,
                      borderRadius: 999,
                      display: 'grid',
                      placeItems: 'center',
                      color: 'primary.main',
                      bgcolor: 'rgba(34,211,238,.12)',
                      border: '1px solid rgba(34,211,238,.28)'
                    }}
                  >
                    {step.icon}
                  </Box>
                  <Typography variant="caption" color="text.secondary" fontWeight={950}>
                    STEP {index + 1}
                  </Typography>
                </Stack>
                <Stack spacing={1}>
                  <Typography variant="h5">{step.title}</Typography>
                  <Typography color="text.secondary">{step.body}</Typography>
                </Stack>
              </Stack>
            </AppCard>
          </Grid>
        ))}
      </Grid>
    </Box>
  );
}

function CoverageBoard() {
  return (
    <AppCard
      sx={{
        overflow: 'hidden',
        backgroundColor: palette.cardSurface
      }}
    >
      <Grid container spacing={0}>
        <Grid size={{ xs: 12, md: 5 }}>
          <Stack
            spacing={2.25}
            sx={{
              height: '100%',
              pr: { md: 3 },
              pb: { xs: 2.5, md: 0 },
              borderRight: { xs: 0, md: `1px solid ${palette.border}` },
              borderBottom: { xs: `1px solid ${palette.border}`, md: 0 }
            }}
          >
            <CoverageHeader icon={<FactCheckOutlinedIcon />} title="Actionable DFS platforms" subtitle="Use these for provider-scoped entry building." accent={palette.primary} />
            <Grid container spacing={1.25}>
              {dfsPlatforms.map((platform) => (
                <Grid key={platform.name} size={{ xs: 12, sm: 6, md: 12 }}>
                  <PlatformBadge platform={platform.name} mark={platform.mark} />
                </Grid>
              ))}
            </Grid>
            <Typography variant="body2" color="text.secondary">
              DFS rows can become entry candidates when they are fresh, positive-edge, and compatible with that provider's rules.
            </Typography>
          </Stack>
        </Grid>
        <Grid size={{ xs: 12, md: 7 }}>
          <Stack spacing={2.25} sx={{ height: '100%', pl: { md: 3 }, pt: { xs: 2.5, md: 0 } }}>
            <CoverageHeader icon={<GridViewOutlinedIcon />} title="Sportsbook reference lines" subtitle="Use these to compare price, line, and consensus." accent={palette.secondary} />
            <Grid container spacing={1}>
              {sportsbookContext.map((book) => (
                <Grid key={book.name} size={{ xs: 12, sm: 6, lg: 4 }}>
                  <SportsbookTile book={book.name} mark={book.mark} />
                </Grid>
              ))}
            </Grid>
            <Typography variant="body2" color="text.secondary">
              Sportsbook reference lines are for comparison only. They are not used to build DFS entries.
            </Typography>
          </Stack>
        </Grid>
      </Grid>
    </AppCard>
  );
}

function CoverageHeader({ icon, title, subtitle, accent }: { icon: ReactNode; title: string; subtitle: string; accent: string }) {
  return (
    <Stack direction="row" alignItems="center" spacing={1.4}>
      <Box
        sx={{
          width: 44,
          height: 44,
          borderRadius: 2,
          display: 'grid',
          placeItems: 'center',
          color: accent,
          bgcolor: `${accent}16`,
          border: `1px solid ${accent}44`
        }}
      >
        {icon}
      </Box>
      <Stack spacing={0.25}>
        <Typography variant="h5">{title}</Typography>
        <Typography variant="caption" color="text.secondary" fontWeight={850}>
          {subtitle}
        </Typography>
      </Stack>
    </Stack>
  );
}

function PlatformBadge({ platform, mark }: { platform: string; mark: string }) {
  return (
    <Stack
      direction="row"
      alignItems="center"
      spacing={1.2}
      sx={{
        p: 1.1,
        borderRadius: 2.5,
        border: '1px solid rgba(34,211,238,.18)',
        bgcolor: 'rgba(8,13,26,.5)'
      }}
    >
      <ProviderLogo provider={platform} category="DFS" mark={mark} size={42} />
      <Stack spacing={0.15}>
        <Typography fontWeight={950}>{platform}</Typography>
        <Typography variant="caption" color="text.secondary">
          Provider-scoped entries
        </Typography>
      </Stack>
    </Stack>
  );
}

function SportsbookTile({ book, mark }: { book: string; mark: string }) {
  return (
    <Stack
      direction="row"
      alignItems="center"
      spacing={1}
      sx={{
        p: 1,
        minHeight: 58,
        borderRadius: 2.25,
        border: '1px solid rgba(148,163,184,.14)',
        bgcolor: 'rgba(8,13,26,.34)'
      }}
    >
      <ProviderLogo provider={book} category="Sportsbook" mark={mark} size={38} />
      <Stack spacing={0.1} minWidth={0}>
        <Typography fontWeight={900} fontSize={13} noWrap>
          {book}
        </Typography>
        <Typography variant="caption" color="text.secondary" noWrap>
          Reference lines
        </Typography>
      </Stack>
    </Stack>
  );
}

function ModifierIntelligence() {
  return (
    <Grid container spacing={2.5} alignItems="stretch">
      <Grid size={{ xs: 12, lg: 5 }}>
        <AppCard sx={{ height: '100%' }}>
          <Stack spacing={2}>
            <Stack direction="row" spacing={1.2} alignItems="center">
              <BoltOutlinedIcon color="primary" />
              <Typography variant="h5">Modifier legend</Typography>
            </Stack>
            <Grid container spacing={1}>
              {modifiers.map((modifier) => (
                <Grid key={modifier.label} size={{ xs: 6, sm: 4, lg: 6 }}>
                  <ModifierBadge modifier={modifier} />
                </Grid>
              ))}
            </Grid>
            <Typography color="text.secondary">
              Modifier badges stay attached to candidate rows anywhere they can affect line context or estimated payout.
            </Typography>
          </Stack>
        </AppCard>
      </Grid>
      <Grid size={{ xs: 12, lg: 7 }}>
        <AppCard sx={{ height: '100%' }}>
          <Stack spacing={2.2}>
            <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ xs: 'flex-start', sm: 'center' }} gap={1.25}>
              <Stack spacing={0.4}>
                <Typography variant="h5" fontSize={{ xs: 23, sm: 26 }}>
                  Estimated payout awareness
                </Typography>
                <Typography color="text.secondary">
                  The app highlights promos and alternate projection states, then reminds you to confirm the final payout in the provider app.
                </Typography>
              </Stack>
              <StatusPill label="Payout may vary" sx={{ alignSelf: { xs: 'flex-start', sm: 'center' }, flexShrink: 0 }} />
            </Stack>
            <Grid container spacing={1.4}>
              {[
                ['Standard line', 'Baseline projection row', '+8.9% EV'],
                ['Demon', 'Higher projection, adjusted payout', 'Variable payout'],
                ['Goblin', 'Lower projection, adjusted payout', 'Variable payout'],
                ['Discounted Line', 'Reduced projection context', '+12.6% EV']
              ].map(([label, detail, value]) => (
                <Grid key={label} size={{ xs: 12, sm: 6 }}>
                  <Box sx={{ p: 1.4, borderRadius: 2, bgcolor: 'rgba(8,13,26,.55)', border: `1px solid ${palette.border}` }}>
                    <Stack direction="row" justifyContent="space-between" gap={1}>
                      <Stack spacing={0.25}>
                        <Typography fontWeight={950}>{label}</Typography>
                        <Typography variant="caption" color="text.secondary">
                          {detail}
                        </Typography>
                      </Stack>
                      <Typography color="primary" fontWeight={950}>
                        {value}
                      </Typography>
                    </Stack>
                  </Box>
                </Grid>
              ))}
            </Grid>
          </Stack>
        </AppCard>
      </Grid>
    </Grid>
  );
}

function ModifierBadge({ modifier }: { modifier: (typeof modifiers)[number] }) {
  return (
    <Stack
      spacing={0.7}
      sx={{
        p: 1.25,
        minHeight: 116,
        borderRadius: 2.5,
        border: `1px solid ${modifier.color}44`,
        bgcolor: `${modifier.color}12`
      }}
    >
      <Box
        sx={{
          width: 38,
          height: 38,
          borderRadius: 2,
          display: 'grid',
          placeItems: 'center',
          color: modifier.color,
          bgcolor: 'rgba(8,13,26,.56)',
          border: `1px solid ${modifier.color}55`
        }}
      >
        {modifier.icon}
      </Box>
      <Stack spacing={0.2}>
        <Typography fontWeight={950} fontSize={13}>
          {modifier.label}
        </Typography>
        <Typography variant="caption" color="text.secondary" lineHeight={1.25}>
          {modifier.detail}
        </Typography>
      </Stack>
    </Stack>
  );
}

function FreshnessTrustPanel() {
  return (
    <Grid container spacing={2.5}>
      <Grid size={{ xs: 12, md: 5 }}>
        <AppCard sx={{ height: '100%' }}>
          <Stack spacing={2}>
            <Stack direction="row" spacing={1.2} alignItems="center">
              <SyncOutlinedIcon color="primary" />
              <Typography variant="h5">Market state monitor</Typography>
            </Stack>
            <Typography color="text.secondary">
              Freshness labels help separate currently useful rows from stale, limited, low-sample, or no-opportunity states.
            </Typography>
            <Stack spacing={1}>
              {statuses.map((status) => (
                <Box key={status.label} sx={{ p: 1.2, borderRadius: 2, bgcolor: 'rgba(8,13,26,.5)', border: `1px solid ${palette.border}` }}>
                  <Stack direction="row" justifyContent="space-between" alignItems="center" gap={1}>
                    <Stack direction="row" spacing={1} alignItems="center">
                      <Box sx={{ width: 9, height: 9, borderRadius: 999, bgcolor: status.tone }} />
                      <Typography fontWeight={950}>{status.label}</Typography>
                    </Stack>
                    <Typography variant="caption" color="text.secondary">
                      {status.detail}
                    </Typography>
                  </Stack>
                </Box>
              ))}
            </Stack>
          </Stack>
        </AppCard>
      </Grid>
      <Grid size={{ xs: 12, md: 7 }}>
        <Grid container spacing={2.5}>
          <Grid size={{ xs: 12, sm: 6 }}>
            <SignalCard icon={<ShieldOutlinedIcon />} title="Trust labels" value="Strong / Medium / Weak" body="Labels reflect sample size, market shape, strategy class, and row-level warnings." />
          </Grid>
          <Grid size={{ xs: 12, sm: 6 }}>
            <SignalCard icon={<WarningAmberOutlinedIcon />} title="Caution states" value="Low sample size" body="Early or thin markets stay visible without being presented as confident candidates." />
          </Grid>
          <Grid size={{ xs: 12 }}>
            <SignalCard icon={<InsightsOutlinedIcon />} title="Performance context" value="Research-first" body="Historical performance summaries are treated as evidence to review, not certainty. Sample-size caution remains visible." />
          </Grid>
        </Grid>
      </Grid>
    </Grid>
  );
}

function SignalCard({ icon, title, value, body }: { icon: ReactNode; title: string; value: string; body: string }) {
  return (
    <AppCard sx={{ height: '100%' }}>
      <Stack spacing={1.3}>
        <Stack direction="row" spacing={1} alignItems="center">
          <Box sx={{ color: 'primary.main', display: 'flex' }}>{icon}</Box>
          <Typography variant="caption" color="text.secondary" fontWeight={950}>
            {title}
          </Typography>
        </Stack>
        <Typography variant="h5">{value}</Typography>
        <Typography color="text.secondary">{body}</Typography>
      </Stack>
    </AppCard>
  );
}

function MiniBar({ label, value }: { label: string; value: number }) {
  return (
    <Stack spacing={0.35}>
      <Stack direction="row" justifyContent="space-between">
        <Typography variant="caption" color="text.secondary">
          {label}
        </Typography>
        <Typography variant="caption" color="text.primary" fontWeight={900}>
          {value}%
        </Typography>
      </Stack>
      <LinearProgress
        variant="determinate"
        value={value}
        sx={{
          height: 6,
          borderRadius: 99,
          bgcolor: 'rgba(148,163,184,.12)',
          '& .MuiLinearProgress-bar': {
            borderRadius: 99,
            backgroundColor: '#22D3EE'
          }
        }}
      />
    </Stack>
  );
}

function LandingSection({ id, eyebrow, title, children }: { id?: string; eyebrow: string; title: string; children: ReactNode }) {
  return (
    <Container id={id} maxWidth="xl" sx={{ position: 'relative', py: { xs: 4.5, md: 7 } }}>
      <Stack spacing={3}>
        <SectionHeader eyebrow={eyebrow} title={title} />
        {children}
      </Stack>
      <Divider sx={{ mt: { xs: 4.5, md: 7 }, opacity: 0.52 }} />
    </Container>
  );
}

import CheckRoundedIcon from '@mui/icons-material/CheckRounded';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import { Box, Button, Checkbox, Collapse, FormControlLabel, MenuItem, Stack, TextField } from '@mui/material';
import { useState } from 'react';

interface MarketFiltersProps {
  useToggleButtons?: boolean;
  classificationLabel?: string;
  candidateOnly?: boolean;
  dfsActionableOnly?: boolean;
  onCandidateOnlyChange?: (value: boolean) => void;
  onDfsActionableOnlyChange?: (value: boolean) => void;
  scannerFields?: boolean;
  compactMobile?: boolean;
}

export function MarketFilters({
  useToggleButtons = false,
  classificationLabel = 'Strategy',
  candidateOnly,
  dfsActionableOnly,
  onCandidateOnlyChange,
  onDfsActionableOnlyChange,
  scannerFields = false,
  compactMobile = false
}: MarketFiltersProps) {
  const [mobileFiltersOpen, setMobileFiltersOpen] = useState(false);
  const selectControls = (
    <>
      {scannerFields ? (
        <TextField label="Sport" size="small" select defaultValue="All" sx={{ minWidth: { xs: '100%', md: 104 }, width: { md: 104 } }}>
          {['All', 'MLB', 'NBA', 'NFL', 'NHL', 'Tennis', 'Golf'].map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
      ) : null}
      {scannerFields ? (
        <TextField label="Provider" size="small" select defaultValue="All" sx={{ minWidth: { xs: '100%', md: 142 }, width: { md: 142 } }}>
          {['All', 'PrizePicks', 'Underdog', 'DraftKings Pick6', 'Betr Picks'].map((item) => (
            <MenuItem key={item} value={item}>
              {item}
            </MenuItem>
          ))}
        </TextField>
      ) : null}
      <TextField label="Provider category" size="small" select defaultValue="All" sx={{ minWidth: { xs: '100%', md: 148 }, width: { md: 148 } }}>
        {['All', 'DFS', 'Reference only'].map((item) => (
          <MenuItem key={item} value={item}>
            {item}
          </MenuItem>
        ))}
      </TextField>
      <TextField label={classificationLabel} size="small" select defaultValue="All" sx={{ minWidth: { xs: '100%', md: 126 }, width: { md: 126 } }}>
        {(classificationLabel === 'Confidence' ? ['All', 'Strong', 'Medium', 'Weak'] : ['All', 'Candidate', 'Watchlist', 'Research', 'Avoid']).map((item) => (
          <MenuItem key={item} value={item}>
            {item}
          </MenuItem>
        ))}
      </TextField>
      {scannerFields ? (
        <>
          <TextField label="Opportunity" size="small" select defaultValue="All" sx={{ minWidth: { xs: '100%', md: 132 }, width: { md: 132 } }}>
            {['All', 'Top Pick', 'Watchlist', 'Research'].map((item) => (
              <MenuItem key={item} value={item}>
                {item}
              </MenuItem>
            ))}
          </TextField>
          <TextField label="Sort" size="small" select defaultValue="EV" sx={{ minWidth: { xs: '100%', md: 104 }, width: { md: 104 } }}>
            {['EV', 'Edge', 'Sample'].map((item) => (
              <MenuItem key={item} value={item}>
                {item}
              </MenuItem>
            ))}
          </TextField>
        </>
      ) : null}
    </>
  );

  return (
    <Box
      sx={{
        border: { xs: '1px solid rgba(148,163,184,.14)', md: 0 },
        borderBottom: { md: '1px solid rgba(148,163,184,.14)' },
        borderRadius: { xs: 3, md: 0 },
        bgcolor: { xs: 'rgba(15,23,42,.42)', md: 'transparent' },
        p: { xs: 1, md: 0 },
        pb: { md: 1.35 }
      }}
    >
      {compactMobile ? (
        <Stack direction="row" spacing={1} sx={{ display: { xs: 'flex', md: 'none' } }}>
          <TextField label="Search player" size="small" fullWidth />
          <Button
            variant="outlined"
            size="small"
            startIcon={<TuneRoundedIcon />}
            onClick={() => setMobileFiltersOpen((open) => !open)}
            sx={{ flexShrink: 0, px: 1.25 }}
          >
            Filters
          </Button>
        </Stack>
      ) : null}
      {compactMobile ? (
        <Collapse in={mobileFiltersOpen} sx={{ display: { xs: 'block', md: 'none' } }}>
          <Stack spacing={1.25} sx={{ pt: 1.25 }}>
            {selectControls}
            {useToggleButtons ? (
              <FilterToggleGroup
                candidateOnly={candidateOnly}
                dfsActionableOnly={dfsActionableOnly}
                onCandidateOnlyChange={onCandidateOnlyChange}
                onDfsActionableOnlyChange={onDfsActionableOnlyChange}
              />
            ) : <CheckboxGroup />}
          </Stack>
        </Collapse>
      ) : null}
      <Stack
        direction={{ xs: 'column', md: 'row' }}
        spacing={{ xs: 1.75, md: 1 }}
        alignItems={{ xs: 'stretch', md: 'center' }}
        useFlexGap
        sx={{
          display: compactMobile ? { xs: 'none', md: 'flex' } : 'flex',
          flexWrap: { md: 'wrap' },
          rowGap: { md: 1 }
        }}
      >
        <TextField
          label="Search player"
          size="small"
          fullWidth
          sx={{
            flex: { md: '1 1 240px' },
            maxWidth: { md: 360 },
            minWidth: { md: 220 }
          }}
        />
        {selectControls}
        {useToggleButtons ? (
          <FilterToggleGroup
            candidateOnly={candidateOnly}
            dfsActionableOnly={dfsActionableOnly}
            onCandidateOnlyChange={onCandidateOnlyChange}
            onDfsActionableOnlyChange={onDfsActionableOnlyChange}
          />
        ) : <CheckboxGroup />}
      </Stack>
    </Box>
  );
}

function FilterToggleGroup({
  candidateOnly,
  dfsActionableOnly,
  onCandidateOnlyChange,
  onDfsActionableOnlyChange
}: {
  candidateOnly?: boolean;
  dfsActionableOnly?: boolean;
  onCandidateOnlyChange?: (value: boolean) => void;
  onDfsActionableOnlyChange?: (value: boolean) => void;
}) {
  const [localCandidateOnly, setLocalCandidateOnly] = useState(candidateOnly ?? true);
  const [localDfsActionableOnly, setLocalDfsActionableOnly] = useState(dfsActionableOnly ?? true);
  const activeCandidateOnly = candidateOnly ?? localCandidateOnly;
  const activeDfsActionableOnly = dfsActionableOnly ?? localDfsActionableOnly;
  const filters = [
    {
      label: 'Candidate only',
      active: activeCandidateOnly,
      onToggle: () => {
        const nextValue = !activeCandidateOnly;
        setLocalCandidateOnly(nextValue);
        onCandidateOnlyChange?.(nextValue);
      }
    },
    {
      label: 'DFS actionable',
      active: activeDfsActionableOnly,
      onToggle: () => {
        const nextValue = !activeDfsActionableOnly;
        setLocalDfsActionableOnly(nextValue);
        onDfsActionableOnlyChange?.(nextValue);
      }
    }
  ];

  return (
    <Stack direction="row" spacing={0.75} useFlexGap sx={{ flex: '0 0 auto', flexWrap: 'nowrap', userSelect: 'none' }}>
      {filters.map((filter) => (
        <Button
          key={filter.label}
          size="small"
          startIcon={filter.active ? <CheckRoundedIcon /> : undefined}
          aria-pressed={filter.active}
          onClick={filter.onToggle}
          sx={{
            height: 34,
            px: 1.25,
            borderRadius: 999,
            border: filter.active ? '1px solid rgba(34,211,238,.34)' : '1px solid rgba(148,163,184,.2)',
            bgcolor: filter.active ? 'rgba(34,211,238,.13)' : 'rgba(148,163,184,.06)',
            color: filter.active ? 'primary.main' : 'text.secondary',
            fontSize: 12.5,
            fontWeight: 900,
            textTransform: 'none',
            whiteSpace: 'nowrap',
            userSelect: 'none',
            '& .MuiButton-startIcon': { mr: 0.45 },
            '&:hover': {
              bgcolor: filter.active ? 'rgba(34,211,238,.18)' : 'rgba(148,163,184,.1)',
              borderColor: filter.active ? 'rgba(34,211,238,.48)' : 'rgba(148,163,184,.32)'
            }
          }}
        >
          {filter.label}
        </Button>
      ))}
    </Stack>
  );
}

function CheckboxGroup() {
  return (
    <Stack direction="row" spacing={0.35} flexWrap="wrap" useFlexGap sx={{ flexShrink: 0 }}>
      <FormControlLabel
        control={<Checkbox defaultChecked size="small" />}
        label="Candidate only"
        sx={{ mr: 0, whiteSpace: 'nowrap', '& .MuiFormControlLabel-label': { fontSize: 13, fontWeight: 800 } }}
      />
      <FormControlLabel
        control={<Checkbox defaultChecked size="small" />}
        label="DFS actionable"
        sx={{ mr: 0, whiteSpace: 'nowrap', '& .MuiFormControlLabel-label': { fontSize: 13, fontWeight: 800 } }}
      />
    </Stack>
  );
}

import CheckRoundedIcon from '@mui/icons-material/CheckRounded';
import TuneRoundedIcon from '@mui/icons-material/TuneRounded';
import { Box, Button, Checkbox, Collapse, FormControlLabel, MenuItem, Stack, TextField } from '@mui/material';
import { type ChangeEvent, useState } from 'react';

export interface MarketFilterOption {
  label: string;
  value: string;
}

export interface MarketFilterValues {
  search: string;
  sport: string;
  provider: string;
  providerCategory: string;
  trust: string;
  opportunity: string;
  sort: string;
  canAddOnly: boolean;
}

interface MarketFiltersProps {
  useToggleButtons?: boolean;
  classificationLabel?: string;
  candidateOnly?: boolean;
  dfsActionableOnly?: boolean;
  dfsActionableLabel?: string;
  onCandidateOnlyChange?: (value: boolean) => void;
  onDfsActionableOnlyChange?: (value: boolean) => void;
  showClassificationFilter?: boolean;
  scannerFields?: boolean;
  compactMobile?: boolean;
  idPrefix?: string;
  values?: MarketFilterValues;
  onValuesChange?: (values: MarketFilterValues) => void;
  sportOptions?: MarketFilterOption[];
  providerOptions?: MarketFilterOption[];
  providerCategoryOptions?: MarketFilterOption[];
  trustOptions?: MarketFilterOption[];
  opportunityOptions?: MarketFilterOption[];
  sortOptions?: MarketFilterOption[];
  showCanAddOnly?: boolean;
  canAddOnlyLabel?: string;
  onClearFilters?: () => void;
}

export function MarketFilters({
  useToggleButtons = false,
  classificationLabel = 'Strategy',
  candidateOnly,
  dfsActionableOnly,
  dfsActionableLabel = 'DFS actionable',
  onCandidateOnlyChange,
  onDfsActionableOnlyChange,
  showClassificationFilter = true,
  scannerFields = false,
  compactMobile = false,
  idPrefix = 'market-filters',
  values,
  onValuesChange,
  sportOptions,
  providerOptions,
  providerCategoryOptions,
  trustOptions,
  opportunityOptions,
  sortOptions,
  showCanAddOnly = false,
  canAddOnlyLabel = 'Playable lines',
  onClearFilters
}: MarketFiltersProps) {
  const [mobileFiltersOpen, setMobileFiltersOpen] = useState(false);
  const controlled = Boolean(values && onValuesChange);

  const updateValue = (key: keyof MarketFilterValues, value: string | boolean) => {
    if (!values || !onValuesChange) return;
    onValuesChange({ ...values, [key]: value });
  };

  const fieldProps = (key: keyof MarketFilterValues, defaultValue = 'All') =>
    controlled
      ? {
          value: values?.[key] ?? defaultValue,
          onChange: (event: ChangeEvent<HTMLInputElement>) => updateValue(key, event.target.value)
        }
      : { defaultValue };

  const accessibilityProps = (key: keyof MarketFilterValues, variant: 'mobile' | 'desktop') => ({
    id: `${idPrefix}-${variant}-${key}`,
    name: `${idPrefix}-${variant}-${key}`
  });

  const renderSelectControls = (variant: 'mobile' | 'desktop') => (
    <>
      {scannerFields ? (
        <TextField
          {...accessibilityProps('sport', variant)}
          label="Sport"
          size="small"
          select
          {...fieldProps('sport')}
          sx={{ minWidth: { xs: '100%', md: 104 }, width: { md: 104 } }}
        >
          {(sportOptions ?? ['All', 'MLB', 'NBA', 'NFL', 'NHL', 'Tennis', 'Golf'].map((item) => ({ label: item, value: item }))).map((item) => (
            <MenuItem key={item.value} value={item.value}>
              {item.label}
            </MenuItem>
          ))}
        </TextField>
      ) : null}
      {scannerFields ? (
        <TextField
          {...accessibilityProps('provider', variant)}
          label="Provider"
          size="small"
          select
          {...fieldProps('provider')}
          sx={{ minWidth: { xs: '100%', md: 142 }, width: { md: 142 } }}
        >
          {(providerOptions ?? ['All', 'PrizePicks', 'Underdog', 'DraftKings Pick6', 'Betr Picks'].map((item) => ({ label: item, value: item }))).map((item) => (
            <MenuItem key={item.value} value={item.value}>
              {item.label}
            </MenuItem>
          ))}
        </TextField>
      ) : null}
      {(providerCategoryOptions?.length ?? 0) > 2 || !controlled ? (
        <TextField
          {...accessibilityProps('providerCategory', variant)}
          label="Provider category"
          size="small"
          select
          {...fieldProps('providerCategory')}
          sx={{ minWidth: { xs: '100%', md: 148 }, width: { md: 148 } }}
        >
          {(providerCategoryOptions ?? ['All', 'DFS', 'Sportsbook'].map((item) => ({ label: item, value: item }))).map((item) => (
            <MenuItem key={item.value} value={item.value}>
              {item.label}
            </MenuItem>
          ))}
        </TextField>
      ) : null}
      {showClassificationFilter ? (
        <TextField
          {...accessibilityProps('trust', variant)}
          label={classificationLabel}
          size="small"
          select
          {...fieldProps('trust')}
          sx={{ minWidth: { xs: '100%', md: 126 }, width: { md: 126 } }}
        >
          {(trustOptions ?? (classificationLabel === 'Trust' ? ['All', 'Strong', 'Medium', 'Weak'] : ['All', 'Candidate', 'Watchlist', 'Research', 'Avoid']).map((item) => ({ label: item, value: item }))).map((item) => (
            <MenuItem key={item.value} value={item.value}>
              {item.label}
            </MenuItem>
          ))}
        </TextField>
      ) : null}
      {scannerFields ? (
        <>
          {(opportunityOptions?.length ?? 0) > 1 || !controlled ? (
            <TextField
              {...accessibilityProps('opportunity', variant)}
              label="Opportunity"
              size="small"
              select
              {...fieldProps('opportunity')}
              sx={{ minWidth: { xs: '100%', md: 132 }, width: { md: 132 } }}
            >
              {(opportunityOptions ?? ['All', 'Top Pick', 'Watchlist', 'Research'].map((item) => ({ label: item, value: item }))).map((item) => (
                <MenuItem key={item.value} value={item.value}>
                  {item.label}
                </MenuItem>
              ))}
            </TextField>
          ) : null}
          <TextField
            {...accessibilityProps('sort', variant)}
            label="Sort"
            size="small"
            select
            {...fieldProps('sort', 'Edge')}
            sx={{ minWidth: { xs: '100%', md: 124 }, width: { md: 124 } }}
          >
            {(sortOptions ?? ['Edge', 'Hit chance'].map((item) => ({ label: item, value: item }))).map((item) => (
              <MenuItem key={item.value} value={item.value}>
                {item.label}
              </MenuItem>
            ))}
          </TextField>
        </>
      ) : null}
    </>
  );

  const toggleControls = controlled ? (
    <Stack direction="row" spacing={0.75} useFlexGap alignItems="center" sx={{ flex: '0 0 auto', flexWrap: 'wrap', userSelect: 'none' }}>
      {candidateOnly !== undefined && onCandidateOnlyChange ? (
        <FilterToggleButton
          label="Candidate only"
          active={candidateOnly}
          onToggle={() => onCandidateOnlyChange(!candidateOnly)}
        />
      ) : null}
      {showCanAddOnly ? (
        <FilterToggleButton
          label={canAddOnlyLabel}
          active={Boolean(values?.canAddOnly)}
          onToggle={() => updateValue('canAddOnly', !values?.canAddOnly)}
        />
      ) : null}
      {onClearFilters ? (
        <Button size="small" variant="text" onClick={onClearFilters} sx={{ color: 'text.secondary', fontWeight: 850, minHeight: 34 }}>
          Clear filters
        </Button>
      ) : null}
    </Stack>
  ) : useToggleButtons ? (
    <FilterToggleGroup
      candidateOnly={candidateOnly}
      dfsActionableOnly={dfsActionableOnly}
      dfsActionableLabel={dfsActionableLabel}
      onCandidateOnlyChange={onCandidateOnlyChange}
      onDfsActionableOnlyChange={onDfsActionableOnlyChange}
    />
  ) : <CheckboxGroup />;

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
          <TextField
            {...accessibilityProps('search', 'mobile')}
            label="Search player"
            size="small"
            fullWidth
            {...(controlled
              ? {
                  value: values?.search ?? '',
                  onChange: (event: ChangeEvent<HTMLInputElement>) => updateValue('search', event.target.value)
                }
              : {})}
          />
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
            {renderSelectControls('mobile')}
            {toggleControls}
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
          {...accessibilityProps('search', 'desktop')}
          label="Search player"
          size="small"
          fullWidth
          {...(controlled
            ? {
                value: values?.search ?? '',
                onChange: (event: ChangeEvent<HTMLInputElement>) => updateValue('search', event.target.value)
              }
            : {})}
          sx={{
            flex: { md: '1 1 240px' },
            maxWidth: { md: 360 },
            minWidth: { md: 220 }
          }}
        />
        {renderSelectControls('desktop')}
        {toggleControls}
      </Stack>
    </Box>
  );
}

function FilterToggleButton({ label, active, onToggle }: { label: string; active: boolean; onToggle: () => void }) {
  return (
    <Button
      size="small"
      startIcon={active ? <CheckRoundedIcon /> : undefined}
      aria-pressed={active}
      onClick={onToggle}
      sx={{
        height: 34,
        px: 1.25,
        borderRadius: 999,
        border: active ? '1px solid rgba(34,211,238,.34)' : '1px solid rgba(148,163,184,.2)',
        bgcolor: active ? 'rgba(34,211,238,.13)' : 'rgba(148,163,184,.06)',
        color: active ? 'primary.main' : 'text.secondary',
        fontSize: 12.5,
        fontWeight: 900,
        textTransform: 'none',
        whiteSpace: 'nowrap',
        userSelect: 'none',
        '& .MuiButton-startIcon': { mr: 0.45 },
        '&:hover': {
          bgcolor: active ? 'rgba(34,211,238,.18)' : 'rgba(148,163,184,.1)',
          borderColor: active ? 'rgba(34,211,238,.48)' : 'rgba(148,163,184,.32)'
        }
      }}
    >
      {label}
    </Button>
  );
}

function FilterToggleGroup({
  candidateOnly,
  dfsActionableOnly,
  dfsActionableLabel,
  onCandidateOnlyChange,
  onDfsActionableOnlyChange
}: {
  candidateOnly?: boolean;
  dfsActionableOnly?: boolean;
  dfsActionableLabel?: string;
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
      label: dfsActionableLabel ?? 'DFS actionable',
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
        control={<Checkbox id="market-filters-uncontrolled-candidate-only" name="market-filters-uncontrolled-candidate-only" defaultChecked size="small" />}
        label="Candidate only"
        sx={{ mr: 0, whiteSpace: 'nowrap', '& .MuiFormControlLabel-label': { fontSize: 13, fontWeight: 800 } }}
      />
      <FormControlLabel
        control={<Checkbox id="market-filters-uncontrolled-dfs-actionable" name="market-filters-uncontrolled-dfs-actionable" defaultChecked size="small" />}
        label="DFS actionable"
        sx={{ mr: 0, whiteSpace: 'nowrap', '& .MuiFormControlLabel-label': { fontSize: 13, fontWeight: 800 } }}
      />
    </Stack>
  );
}

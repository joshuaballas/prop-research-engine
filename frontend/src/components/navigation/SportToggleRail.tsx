import { Box, Button, Stack, Typography } from '@mui/material';
import type { SportCode, SportOption } from '../../types/sports';
import { palette } from '../../theme/palette';

interface SportToggleRailProps {
  selectedSports: SportCode[];
  onToggle: (sport: SportCode) => void;
  compact?: boolean;
  sports?: SportOption[];
  hideStatuses?: boolean;
}

export function SportToggleRail({ selectedSports, onToggle, compact = false, sports = [], hideStatuses = true }: SportToggleRailProps) {
  return (
    <Box sx={{ overflowX: 'auto', pb: 0.5 }}>
      <Stack direction="row" spacing={1} minWidth="max-content">
        {sports.map((sport) => {
          const active = selectedSports.includes(sport.code);
          return (
            <Button
              key={sport.code}
              variant="outlined"
              onClick={() => onToggle(sport.code)}
              sx={{
                minWidth: compact ? 104 : 122,
                px: compact ? 1.15 : 1.4,
                py: compact ? 0.72 : 1,
                justifyContent: 'flex-start',
                borderRadius: compact ? 1.75 : 2.25,
                borderColor: active ? 'rgba(34,211,238,.58)' : palette.border,
                bgcolor: active ? 'rgba(34,211,238,.12)' : 'rgba(15,23,42,.55)',
                color: active ? 'primary.main' : 'text.secondary',
                boxShadow: 'none',
                '&:hover': {
                  borderColor: active ? 'rgba(34,211,238,.72)' : 'rgba(148,163,184,.32)',
                  bgcolor: active ? 'rgba(34,211,238,.15)' : 'rgba(15,23,42,.78)'
                }
              }}
            >
              <Stack direction="row" spacing={1} alignItems="center">
                <Box
                  component="span"
                  sx={{
                    width: compact ? 22 : 26,
                    height: compact ? 22 : 26,
                    borderRadius: 1.5,
                    display: 'grid',
                    placeItems: 'center',
                    bgcolor: active ? 'rgba(34,211,238,.16)' : 'rgba(148,163,184,.08)'
                  }}
                >
                  {sport.icon}
                </Box>
                <Box textAlign="left">
                  <Typography variant="body2" fontWeight={900} lineHeight={1.1}>
                    {sport.label}
                  </Typography>
                  {!hideStatuses ? (
                    <Typography variant="caption" color="text.secondary" lineHeight={1} sx={{ display: compact ? { xs: 'none', sm: 'block' } : 'block' }}>
                      {(sport.status === 'No data' ? 'No markets' : sport.status) ?? `${sport.candidateCount} candidates`}
                    </Typography>
                  ) : null}
                </Box>
              </Stack>
            </Button>
          );
        })}
      </Stack>
    </Box>
  );
}

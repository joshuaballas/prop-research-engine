import { Box, Button, Stack, Typography } from '@mui/material';
import { mockSports } from '../../data/mockSports';
import type { SportCode } from '../../types/sports';
import { palette } from '../../theme/palette';

interface SportToggleRailProps {
  selectedSports: SportCode[];
  onToggle: (sport: SportCode) => void;
}

export function SportToggleRail({ selectedSports, onToggle }: SportToggleRailProps) {
  return (
    <Box sx={{ overflowX: 'auto', pb: 0.5 }}>
      <Stack direction="row" spacing={1} minWidth="max-content">
        {mockSports.map((sport) => {
          const active = selectedSports.includes(sport.code);
          return (
            <Button
              key={sport.code}
              variant={active ? 'contained' : 'outlined'}
              onClick={() => onToggle(sport.code)}
              sx={{
                minWidth: 104,
                justifyContent: 'flex-start',
                borderColor: active ? 'transparent' : palette.border,
                bgcolor: active ? undefined : 'rgba(15,23,42,.55)'
              }}
            >
              <Stack direction="row" spacing={1} alignItems="center">
                <span>{sport.icon}</span>
                <Box textAlign="left">
                  <Typography variant="body2" fontWeight={900} lineHeight={1.1}>
                    {sport.label}
                  </Typography>
                  <Typography variant="caption" color={active ? '#031019' : 'text.secondary'} lineHeight={1}>
                    {sport.status ?? `${sport.candidateCount} candidates`}
                  </Typography>
                </Box>
              </Stack>
            </Button>
          );
        })}
      </Stack>
    </Box>
  );
}

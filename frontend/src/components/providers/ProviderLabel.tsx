import { Stack, Typography, type SxProps, type Theme } from '@mui/material';
import type { ProviderCategory } from '../../types/markets';
import { ProviderLogo } from './ProviderLogo';

interface ProviderLabelProps {
  provider: string;
  category?: ProviderCategory;
  compact?: boolean;
  sx?: SxProps<Theme>;
}

export function ProviderLabel({ provider, category = 'DFS', compact = false, sx }: ProviderLabelProps) {
  return (
    <Stack direction="row" alignItems="center" spacing={0.75} sx={sx}>
      <ProviderLogo provider={provider} category={category} size={compact ? 24 : 30} />
      <Typography variant="body2" fontWeight={900} noWrap>
        {formatProviderName(provider)}
      </Typography>
    </Stack>
  );
}

function formatProviderName(provider: string) {
  const replacements: Record<string, string> = {
    BetrPicks: 'Betr Picks',
    DraftKingsPick6: 'DraftKings Pick6',
    SportsbookContext: 'Sportsbook Context'
  };

  return replacements[provider] ?? provider;
}

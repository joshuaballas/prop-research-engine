import { Stack, Typography, type SxProps, type Theme } from '@mui/material';
import type { MarketOpportunity } from '../../types/markets';
import { formatSelectedMarketAmericanOdds } from '../../utils/marketPriceDisplay';
import { AppTableSecondaryText } from '../ui/AppTableTypography';
import { OpportunityPickText } from '../ui/OpportunityCard';

type MarketPickPriceProps = {
  market: Pick<MarketOpportunity, 'side' | 'line' | 'propType' | 'providerCategory' | 'bestAmericanPrice' | 'effectiveAmericanOdds'>;
  variant?: 'table' | 'card';
  sx?: SxProps<Theme>;
};

const tablePickSx = {
  fontSize: 13,
  flexWrap: 'nowrap',
  overflow: 'hidden',
  '& .MuiTypography-root': { fontSize: 'inherit', fontWeight: 500 }
};

export function MarketPickPrice({ market, variant = 'table', sx }: MarketPickPriceProps) {
  const priceText = formatSelectedMarketAmericanOdds(market, null);

  return (
    <Stack spacing={0.15} minWidth={0} sx={sx}>
      <OpportunityPickText
        side={market.side}
        propType={market.propType}
        line={market.line}
        sx={variant === 'table' ? tablePickSx : undefined}
      />
      {priceText ? (
        variant === 'table' ? (
          <AppTableSecondaryText>{priceText}</AppTableSecondaryText>
        ) : (
          <Typography variant="caption" color="text.secondary" fontWeight={650} lineHeight={1.25}>
            {priceText}
          </Typography>
        )
      ) : null}
    </Stack>
  );
}

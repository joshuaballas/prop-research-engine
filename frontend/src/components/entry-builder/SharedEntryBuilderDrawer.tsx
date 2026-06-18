import { Box, Button, Drawer, useMediaQuery, useTheme } from '@mui/material';
import { mockEntryRules } from '../../data/mockEntryRules';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import { palette } from '../../theme/palette';
import { EntryBuilderTray } from './EntryBuilderTray';

export function SharedEntryBuilderDrawer() {
  const builder = useEntryBuilder();
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('md'));
  const lockedProvider = builder.lockedProviderName;
  const averageEv = builder.legs.length ? builder.legs.reduce((sum, leg) => sum + leg.estimatedEv, 0) / builder.legs.length : 0;

  return (
    <>
      <Drawer
        anchor={isMobile ? 'bottom' : 'right'}
        variant={isMobile ? 'temporary' : 'persistent'}
        open={builder.isDrawerOpen}
        onClose={isMobile ? builder.closeEntryBuilder : undefined}
        ModalProps={{ keepMounted: true }}
        PaperProps={{
          sx: {
            width: { xs: '100%', md: 360 },
            top: { md: '67px' },
            height: { xs: '82vh', md: 'calc(100% - 67px)' },
            maxHeight: { xs: 680, md: 'none' },
            borderTopLeftRadius: { xs: 4, md: 0 },
            borderTopRightRadius: { xs: 4, md: 0 },
            borderLeft: { md: '1px solid rgba(125,211,252,.22)' },
            borderTop: { xs: '1px solid rgba(125,211,252,.22)', md: 'none' },
            backgroundColor: palette.cardSurface
          }
        }}
      >
        <EntryBuilderTray
          legs={builder.legs}
          rules={mockEntryRules}
          amount={builder.amount}
          onAmountChange={builder.setAmount}
          onRemove={builder.removeLeg}
          onClear={builder.clearEntry}
          onClose={builder.closeEntryBuilder}
        />
      </Drawer>

      {builder.legs.length && !builder.isDrawerOpen ? (
        <Button
          variant="contained"
          onClick={builder.openEntryBuilder}
          sx={{
            position: 'fixed',
            zIndex: (item) => item.zIndex.drawer - 1,
            right: { xs: 16, md: 24 },
            left: { xs: 16, md: 'auto' },
            bottom: { xs: 'calc(72px + env(safe-area-inset-bottom))', md: 24 },
            justifyContent: 'space-between',
            px: 2,
            py: 1.1,
            boxShadow: '0 14px 32px rgba(0,0,0,.3)'
          }}
        >
          <Box component="span">
            {isMobile ? `${lockedProvider} - ${builder.legs.length} legs - Avg EV ${formatSignedPercent(averageEv)}` : `Entry: ${lockedProvider} - ${builder.legs.length} legs`}
          </Box>
        </Button>
      ) : null}
    </>
  );
}

function formatSignedPercent(value: number) {
  const formatted = `${Math.abs(value * 100).toFixed(1)}%`;
  return value >= 0 ? `+${formatted}` : `-${formatted}`;
}

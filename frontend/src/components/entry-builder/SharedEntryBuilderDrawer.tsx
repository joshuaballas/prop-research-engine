import { Box, Button, Drawer, useMediaQuery, useTheme } from '@mui/material';
import { useEffect, useState } from 'react';
import { getEntryRules } from '../../api/entryRules';
import { mockEntryRules } from '../../data/mockEntryRules';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import { palette } from '../../theme/palette';
import type { EntryRule } from '../../types/entryBuilder';
import { EntryBuilderTray } from './EntryBuilderTray';

export function SharedEntryBuilderDrawer() {
  const builder = useEntryBuilder();
  const theme = useTheme();
  const isMobile = useMediaQuery(theme.breakpoints.down('md'));
  const lockedProvider = builder.lockedProviderName;
  const averageEdge = builder.legs.length ? builder.legs.reduce((sum, leg) => sum + (leg.probabilityEdge ?? 0), 0) / builder.legs.length : 0;
  const [rules, setRules] = useState<EntryRule[]>(mockEntryRules);

  useEffect(() => {
    const controller = new AbortController();
    getEntryRules(controller.signal)
      .then((nextRules) => {
        if (nextRules.length) setRules(nextRules);
      })
      .catch(() => {
        setRules(mockEntryRules);
      });

    return () => controller.abort();
  }, []);

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
            top: { md: 0 },
            height: { xs: '82dvh', md: '100vh' },
            maxHeight: { xs: 680, md: 'none' },
            borderTopLeftRadius: { xs: 4, md: 0 },
            borderTopRightRadius: { xs: 4, md: 0 },
            borderLeft: { md: '1px solid rgba(125,211,252,.22)' },
            borderTop: { xs: '1px solid rgba(125,211,252,.22)', md: 'none' },
            backgroundColor: palette.bg,
            backgroundImage: 'none'
          }
        }}
      >
        <EntryBuilderTray
          legs={builder.legs}
          rules={rules}
          amount={builder.amount}
          selectedEntryType={builder.selectedEntryType}
          onAmountChange={builder.setAmount}
          onEntryTypeChange={builder.setSelectedEntryType}
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
            right: { xs: 16, md: 'auto' },
            left: { xs: 16, md: 24 },
            bottom: { xs: 'calc(72px + env(safe-area-inset-bottom))', md: 24 },
            justifyContent: 'space-between',
            px: 2,
            py: 1.1,
            boxShadow: 'none'
          }}
        >
          <Box component="span">
            {isMobile ? `${lockedProvider} - ${builder.legs.length} legs - Avg Edge ${formatSignedPercent(averageEdge)}` : `Entry: ${lockedProvider} - ${builder.legs.length} legs`}
          </Box>
        </Button>
      ) : null}
    </>
  );
}

function formatSignedPercent(value: number | null | undefined) {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—';
  const formatted = `${Math.abs(value * 100).toFixed(1)}%`;
  return value >= 0 ? `+${formatted}` : `-${formatted}`;
}

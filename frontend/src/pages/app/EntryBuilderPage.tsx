import { useEffect, useState } from 'react';
import { Stack } from '@mui/material';
import { getEntryRules } from '../../api/entryRules';
import { EntryBuilderTray } from '../../components/entry-builder/EntryBuilderTray';
import { AppCard } from '../../components/ui/AppCard';
import { PageHeader } from '../../components/ui/PageHeader';
import { mockEntryRules } from '../../data/mockEntryRules';
import { useEntryBuilder } from '../../hooks/useEntryBuilder';
import type { EntryRule } from '../../types/entryBuilder';

export function EntryBuilderPage() {
  const builder = useEntryBuilder();
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
    <Stack spacing={3} sx={{ pb: { xs: 'calc(92px + env(safe-area-inset-bottom))', lg: 0 } }}>
      <PageHeader
        title="Entry Builder"
        description="Build a compatible entry from playable rows, review selected legs, and confirm provider rules before submitting manually."
      />

      <AppCard contentSx={{ p: 0, '&:last-child': { pb: 0 } }} sx={{ minHeight: { xs: 620, md: 720 }, overflow: 'hidden' }}>
        <EntryBuilderTray
          legs={builder.legs}
          rules={rules}
          amount={builder.amount}
          selectedEntryType={builder.selectedEntryType}
          onAmountChange={builder.setAmount}
          onEntryTypeChange={builder.setSelectedEntryType}
          onRemove={builder.removeLeg}
          onClear={builder.clearEntry}
        />
      </AppCard>
    </Stack>
  );
}

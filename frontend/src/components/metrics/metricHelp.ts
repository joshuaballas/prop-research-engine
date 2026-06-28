export type MetricHelpKey = 'edge' | 'hitChance' | 'sample' | 'warning' | 'caution';

export const metricHelp: Record<MetricHelpKey, { label: string; description: string }> = {
  edge: {
    label: 'Edge',
    description: 'Probability edge: model hit chance minus implied probability from the selected book.'
  },
  hitChance: {
    label: 'Hit chance',
    description: 'Model-estimated chance that the selected side hits. If unavailable, historical hit rate is still shown in the chart.'
  },
  sample: {
    label: 'Sample',
    description: "Number of historical results used for this pick's context. Larger samples usually make the signal more reliable, but they do not guarantee an outcome."
  },
  warning: {
    label: 'Caution',
    description: 'Short reasons this line may need extra review or may not be addable.'
  },
  caution: {
    label: 'Caution',
    description: 'Short reasons this line may need extra review or may not be addable.'
  }
};

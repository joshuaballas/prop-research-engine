export type MetricHelpKey = 'ev' | 'edge' | 'hitChance' | 'trust' | 'sample' | 'warning' | 'caution';

export const metricHelp: Record<MetricHelpKey, { label: string; description: string }> = {
  ev: {
    label: 'EV',
    description: 'EV is only shown for sportsbook-priced markets. DFS props use hit chance, edge, and trust instead.'
  },
  edge: {
    label: 'Edge',
    description: 'Difference between our hit chance and the break-even chance implied by the provider line.'
  },
  hitChance: {
    label: 'Hit chance',
    description: 'Model-estimated chance that the selected side hits.'
  },
  trust: {
    label: 'Trust',
    description: 'Reliability of the estimate based on sample size, model confidence, and data quality.'
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

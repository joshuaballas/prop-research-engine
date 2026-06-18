export type MetricHelpKey = 'ev' | 'edge' | 'hitChance' | 'trust' | 'warning';

export const metricHelp: Record<MetricHelpKey, { label: string; description: string }> = {
  ev: {
    label: 'EV',
    description: 'Estimated expected value for the selected side. Higher is better.'
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
  warning: {
    label: 'Warning',
    description: 'Caution flags that may make the pick less reliable, such as low sample size.'
  }
};

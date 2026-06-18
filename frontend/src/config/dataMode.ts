export type DataMode = 'api' | 'demo';

export const dataMode: DataMode = import.meta.env.VITE_DATA_MODE === 'demo' ? 'demo' : 'api';

export const isDemoDataMode = dataMode === 'demo';

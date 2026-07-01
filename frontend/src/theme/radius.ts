export const radius = {
  none: 0,
  subtle: 0.5,
  control: 1,
  table: 1.25,
  card: 1.5,
  featuredCard: 2,
  modal: 2,
  pill: 999,
  circle: '50%'
} as const;

export const themeShapeBorderRadius = 5;

export function themeRadius(value: number) {
  return value * themeShapeBorderRadius;
}

import { Checkbox, FormControlLabel, MenuItem, Stack, TextField } from '@mui/material';

export function MarketFilters() {
  return (
    <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.5}>
      <TextField label="Search player" size="small" fullWidth />
      <TextField label="Provider category" size="small" select defaultValue="All" sx={{ minWidth: 180 }}>
        {['All', 'DFS', 'Sportsbook'].map((item) => (
          <MenuItem key={item} value={item}>
            {item}
          </MenuItem>
        ))}
      </TextField>
      <TextField label="Strategy" size="small" select defaultValue="All" sx={{ minWidth: 180 }}>
        {['All', 'Candidate', 'Watchlist', 'Research', 'Avoid'].map((item) => (
          <MenuItem key={item} value={item}>
            {item}
          </MenuItem>
        ))}
      </TextField>
      <FormControlLabel control={<Checkbox defaultChecked />} label="Candidate only" />
      <FormControlLabel control={<Checkbox defaultChecked />} label="DFS actionable" />
    </Stack>
  );
}

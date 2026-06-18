import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import { Box, Dialog, DialogContent, DialogTitle, IconButton, Stack, Typography } from '@mui/material';
import { trustColor, type TrustColorLevel } from '../../utils/trustColors';
import { glossaryTerms } from './glossaryTerms';

interface GlossaryDialogProps {
  open: boolean;
  onClose: () => void;
}

export function GlossaryDialog({ open, onClose }: GlossaryDialogProps) {
  return (
    <Dialog
      open={open}
      onClose={onClose}
      fullWidth
      maxWidth="md"
      aria-labelledby="market-glossary-title"
      PaperProps={{
        sx: {
          borderRadius: 2.5,
          border: '1px solid rgba(148,163,184,.18)',
          bgcolor: 'rgba(8,13,25,.98)',
          backgroundImage: 'linear-gradient(180deg, rgba(17,24,39,.98), rgba(8,13,25,.98))',
          boxShadow: '0 28px 80px rgba(0,0,0,.48)'
        }
      }}
    >
      <DialogTitle id="market-glossary-title" sx={{ pr: 7, pb: 1 }}>
        <Stack spacing={0.35}>
          <Typography variant="h4">Glossary</Typography>
          <Typography variant="body2" color="text.secondary">
            Quick definitions for market metrics, provider lines, and entry terms. These are decision-support signals, not guarantees.
          </Typography>
        </Stack>
        <IconButton
          aria-label="Close glossary"
          onClick={onClose}
          sx={{
            position: 'absolute',
            right: 14,
            top: 14,
            color: 'text.secondary',
            '&:hover': { color: 'text.primary', bgcolor: 'rgba(148,163,184,.10)' }
          }}
        >
          <CloseRoundedIcon />
        </IconButton>
      </DialogTitle>
      <DialogContent sx={{ pt: 1, pb: 3 }}>
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' },
            gap: 1.1
          }}
        >
          {glossaryTerms.map((term) => (
            <Box
              key={term.label}
              sx={{
                p: 1.5,
                borderRadius: 2,
                border: '1px solid rgba(148,163,184,.14)',
                bgcolor: 'rgba(15,23,42,.58)'
              }}
            >
              <Typography fontWeight={900}>{term.label}</Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mt: 0.35 }}>
                {term.definition}
              </Typography>
              {term.details?.length ? (
                <Stack spacing={0.35} sx={{ mt: 1 }}>
                  {term.details.map((detail) => (
                    <Typography key={detail.label} variant="caption" color="text.secondary">
                      <Box component="span" sx={{ color: trustDetailColor(detail.label), fontWeight: 850 }}>
                        {detail.label}:
                      </Box>{' '}
                      {detail.definition}
                    </Typography>
                  ))}
                </Stack>
              ) : null}
            </Box>
          ))}
        </Box>
      </DialogContent>
    </Dialog>
  );
}

function trustDetailColor(label: string) {
  if (label === 'Low' || label === 'Medium' || label === 'High') {
    return trustColor(label as TrustColorLevel);
  }

  return 'text.primary';
}

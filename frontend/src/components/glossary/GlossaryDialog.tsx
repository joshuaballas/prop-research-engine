import CloseRoundedIcon from '@mui/icons-material/CloseRounded';
import { Box, Dialog, DialogContent, DialogTitle, IconButton, Stack, Typography } from '@mui/material';
import { palette } from '../../theme/palette';
import { radius } from '../../theme/radius';
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
          borderRadius: radius.modal,
          border: '1px solid rgba(148,163,184,.18)',
          bgcolor: palette.bg,
          backgroundImage: 'none',
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
      <DialogContent className="app-scrollbar" sx={{ pt: 1, pb: 3 }}>
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
                borderRadius: radius.card,
                border: '1px solid rgba(148,163,184,.14)',
                bgcolor: palette.cardSurface
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
                      <Box component="span" sx={{ color: 'text.primary', fontWeight: 850 }}>
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

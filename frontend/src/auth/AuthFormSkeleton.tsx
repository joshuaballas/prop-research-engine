import { Box, Skeleton, Stack } from '@mui/material';
import { palette } from '../theme/palette';
import { radius } from '../theme/radius';

export type AuthFormSkeletonVariant = 'signIn' | 'signUp';

export const authFormSlotMinHeight = {
  signIn: { xs: 330, sm: 360 },
  signUp: { xs: 500, sm: 540 }
} as const;

const skeletonSx = {
  bgcolor: 'rgba(148, 163, 184, 0.12)'
} as const;

export function AuthFormSkeleton({ variant }: { variant: AuthFormSkeletonVariant }) {
  const isSignUp = variant === 'signUp';

  return (
    <Box
      aria-hidden="true"
      sx={{
        minHeight: authFormSlotMinHeight[variant],
        display: 'flex',
        flexDirection: 'column',
        justifyContent: 'space-between',
        width: '100%',
        py: 0.5
      }}
    >
      <Stack spacing={isSignUp ? 1.25 : 1.45}>
        {isSignUp ? (
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 1 }}>
            <FormFieldSkeleton labelWidth="42%" />
            <FormFieldSkeleton labelWidth="42%" />
          </Box>
        ) : null}

        <FormFieldSkeleton labelWidth={isSignUp ? '34%' : '28%'} />
        {isSignUp ? <FormFieldSkeleton labelWidth="38%" /> : null}

        <Skeleton
          animation="wave"
          variant="rounded"
          height={38}
          sx={{ ...skeletonSx, borderRadius: radius.control, mt: 0.35 }}
        />

        <Stack direction="row" alignItems="center" spacing={1.2} sx={{ pt: 0.4 }}>
          <Box sx={{ height: 1, flex: 1, bgcolor: 'rgba(148, 163, 184, 0.16)' }} />
          <Skeleton animation="wave" width={52} height={16} sx={skeletonSx} />
          <Box sx={{ height: 1, flex: 1, bgcolor: 'rgba(148, 163, 184, 0.16)' }} />
        </Stack>
      </Stack>

      <Box
        sx={{
          mt: 2,
          pt: 1.3,
          borderTop: `1px solid ${palette.border}`
        }}
      >
        <Stack direction="row" justifyContent="center" spacing={0.8}>
          <Skeleton animation="wave" width={isSignUp ? 118 : 132} height={18} sx={skeletonSx} />
          <Skeleton animation="wave" width={isSignUp ? 48 : 56} height={18} sx={skeletonSx} />
        </Stack>
      </Box>
    </Box>
  );
}

function FormFieldSkeleton({ labelWidth }: { labelWidth: string }) {
  return (
    <Stack spacing={0.45}>
      <Skeleton animation="wave" width={labelWidth} height={16} sx={skeletonSx} />
      <Skeleton animation="wave" variant="rounded" height={42} sx={{ ...skeletonSx, borderRadius: radius.control }} />
    </Stack>
  );
}

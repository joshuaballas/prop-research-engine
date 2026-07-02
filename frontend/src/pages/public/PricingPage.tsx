import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded';
import { Accordion, AccordionDetails, AccordionSummary, Box, Container, Skeleton, Stack, Typography } from '@mui/material';
import { getPublicPlans } from '../../api/billing';
import { mapPlanDtoToPricingPlan } from '../../api/planMappers';
import { useApiQuery } from '../../api/useApiQuery';
import { PlanCard } from '../../components/billing/PlanCard';
import { AppCard } from '../../components/ui/AppCard';

const pricingFaqItems = [
  {
    question: 'Is paid checkout available yet?',
    answer: 'No. Paid checkout is not available yet.'
  },
  {
    question: 'What is the Free plan for?',
    answer: 'Trying the basic workflow before paid access is enabled.'
  },
  {
    question: 'What does Pro add?',
    answer: 'Top Picks, line shopping, model performance, and entry review workflows.'
  },
  {
    question: 'Can I change plans later?',
    answer: 'Plan setup can be reviewed from Billing.'
  },
  {
    question: 'Is this betting advice?',
    answer: 'No. This is a research tool. Lines, odds, payouts, and availability should be confirmed in the provider app before submitting.'
  }
];
const pricingContentMaxWidth = 900;

export function PricingPage() {
  const plansQuery = useApiQuery(getPublicPlans);
  const plans = plansQuery.data?.plans.map(mapPlanDtoToPricingPlan) ?? [];

  return (
    <Container maxWidth="lg" sx={{ py: { xs: 4, md: 7 } }}>
      <Box sx={{ width: '100%', maxWidth: pricingContentMaxWidth, marginInline: 'auto' }}>
        <Stack spacing={{ xs: 3.5, md: 4.5 }}>
        <Stack spacing={1.35} textAlign="center" alignItems="center">
          <Typography variant="h2" maxWidth={760}>
            Choose your plan
          </Typography>
          <Typography color="text.secondary" maxWidth={720} fontSize={{ md: 18 }}>
            Paid checkout is not available yet.
          </Typography>
        </Stack>

        <Box
          sx={{
            width: '100%',
            display: 'grid',
            gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' },
            gap: 2.5,
            alignItems: 'stretch'
          }}
        >
          {plansQuery.loading ? Array.from({ length: 2 }).map((_, index) => (
            <Skeleton key={index} variant="rounded" height={420} />
          )) : plans.map((plan) => (
            <Box key={plan.code} sx={{ display: 'flex', minWidth: 0 }}>
              <PlanCard plan={plan} />
            </Box>
          ))}
        </Box>
        {!plansQuery.loading && plans.length === 0 ? (
          <Typography color="text.secondary" textAlign="center">
            Plans are not available right now.
          </Typography>
        ) : null}

        <AppCard sx={{ width: '100%' }}>
          <Stack spacing={1.35}>
            <Typography variant="h4">Questions before you choose</Typography>

            <Box>
              {pricingFaqItems.map((item, index) => (
                <Accordion
                  key={item.question}
                  disableGutters
                  elevation={0}
                  sx={{
                    backgroundColor: 'transparent',
                    backgroundImage: 'none',
                    boxShadow: 'none',
                    color: 'text.primary',
                    borderTop: index === 0 ? '1px solid rgba(148,163,184,.16)' : '0',
                    borderBottom: '1px solid rgba(148,163,184,.16)',
                    '&::before': { display: 'none' },
                    '&.Mui-expanded': { m: 0 }
                  }}
                >
                  <AccordionSummary
                    expandIcon={<ExpandMoreRoundedIcon />}
                    sx={{
                      minHeight: 50,
                      px: 0,
                      py: 0.25,
                      '&.Mui-expanded': { minHeight: 50 },
                      '& .MuiAccordionSummary-content': {
                        my: 1,
                        alignItems: 'center'
                      },
                      '& .MuiAccordionSummary-content.Mui-expanded': {
                        my: 1
                      },
                      '& .MuiAccordionSummary-expandIconWrapper': {
                        color: 'text.secondary'
                      },
                      '& .MuiAccordionSummary-expandIconWrapper.Mui-expanded': {
                        color: 'primary.main'
                      }
                    }}
                  >
                    <Typography fontWeight={900}>{item.question}</Typography>
                  </AccordionSummary>
                  <AccordionDetails sx={{ px: 0, pt: 0, pb: 1.6 }}>
                    <Typography color="text.secondary">
                      {item.answer}
                    </Typography>
                  </AccordionDetails>
                </Accordion>
              ))}
            </Box>
          </Stack>
        </AppCard>
        </Stack>
      </Box>
    </Container>
  );
}

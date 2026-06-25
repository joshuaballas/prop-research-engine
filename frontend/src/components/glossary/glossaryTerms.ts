export interface GlossaryTerm {
  label: string;
  definition: string;
  details?: Array<{ label: string; definition: string }>;
}

export const glossaryTerms: GlossaryTerm[] = [
  {
    label: 'EV',
    definition:
      'Estimated expected value for sportsbook-priced markets. DFS props hide EV until a verified DFS payout or break-even model is available.'
  },
  {
    label: 'Edge',
    definition:
      "The gap between our estimated hit chance and the break-even chance implied by the provider line or price. A positive edge means the model thinks the side hits more often than the provider's line requires to be worthwhile."
  },
  {
    label: 'Hit chance',
    definition:
      "The model-estimated probability that the selected side of the prop hits. For example, if a player's strikeout line is 6.5 and the side is Over, this is the estimated chance the player finishes with 7 or more strikeouts."
  },
  {
    label: 'Trust',
    definition:
      'How reliable the app thinks the estimate is. Trust is based on supporting evidence such as sample size, model confidence, data quality, and whether the market is stale, thin, or missing important context.',
    details: [
      {
        label: 'Low',
        definition:
          'Limited evidence or weaker data quality. Low trust does not mean the pick is bad; it means the estimate needs extra caution.'
      },
      {
        label: 'Medium',
        definition:
          'Usable signal with enough support to review, but not strong enough to treat as highly reliable on its own.'
      },
      {
        label: 'High',
        definition:
          'Stronger supporting evidence, cleaner data, and better model confidence. High trust still does not guarantee the pick will hit.'
      }
    ]
  },
  {
    label: 'Caution',
    definition:
      'A short reason a line may need extra review or may not be addable, such as low sample size, stale data, locked entries, or provider availability issues.'
  },
  {
    label: 'Low sample',
    definition:
      'The app has limited historical evidence for this player, prop, line, or context. The estimate may still be useful, but it should not be treated with the same confidence as a larger sample.'
  },
  {
    label: 'Provider',
    definition:
      'The sportsbook or DFS platform offering the line, such as PrizePicks, Underdog, Betr Picks, or DraftKings Pick6.'
  },
  {
    label: 'Line',
    definition:
      'The target number for a prop. For example, Pitcher Strikeouts 6.5 means the selected side is judged against that number.'
  },
  {
    label: 'Reference line',
    definition:
      "A comparison line from another source used for context. Reference lines help show whether a provider's line looks better or worse than the broader market, but they may not always be available."
  },
  {
    label: 'Entry',
    definition:
      'A planned provider-compatible slip made up of selected legs. The app can help organize and track an entry, but it does not place bets or submit DFS entries for the user.'
  },
  {
    label: 'Leg',
    definition: 'One selected prop inside an entry. Example: Bryan Woo Under Pitcher Strikeouts 6.5.'
  },
  {
    label: 'Top pick',
    definition:
      'A high-priority opportunity surfaced by the app based on edge, hit chance, trust, caution state, and current line context. Top pick does not mean guaranteed winner.'
  },
  {
    label: 'Best DFS line',
    definition:
      'The strongest available DFS provider line for the selected market context. This usually means the line with the most favorable combination of hit chance, payout or price, and provider compatibility.'
  },
  {
    label: 'Stale',
    definition:
      'Market data that may be old enough to require extra caution. A stale line may have moved, disappeared, or changed since the last successful refresh.'
  },
  {
    label: 'Pulled',
    definition:
      'A provider line that was seen before but is no longer available from the source. Pulled lines should not be treated as actionable.'
  },
  {
    label: 'Locked',
    definition:
      'A leg or entry state that cannot be added or changed because of provider compatibility, timing, or entry rules.'
  },
  {
    label: 'Hit rate',
    definition:
      "The historical share of comparable results that cleared the selected line. Hit rate describes past outcomes; hit chance is the model's estimate for the current opportunity."
  },
  {
    label: 'Break-even chance',
    definition:
      'The hit chance required for a line or payout to be neutral before model edge is considered. If our hit chance is higher than break-even, the opportunity may have positive edge.'
  },
  {
    label: 'DFS',
    definition:
      'Daily fantasy sports contests where users build entries from player props or stat outcomes. Provider rules vary by platform.'
  },
  {
    label: 'Provider-compatible',
    definition:
      'Structured so the selected legs follow the rules for the chosen provider, such as allowed sports, leg counts, and entry types.'
  }
];

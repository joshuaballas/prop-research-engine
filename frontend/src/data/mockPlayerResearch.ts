import type { MarketOpportunity } from '../types/markets';
import type { SportCode } from '../types/sports';

export interface PlayerResearchLogEntry {
  date: string;
  event: string;
  value: string;
  line?: string;
  result?: 'Cleared' | 'Missed' | 'Push' | 'No line';
}

export interface PlayerResearchStat {
  id: string;
  label: string;
  side?: 'Over' | 'Under';
  currentLine?: string;
  last5Average: string;
  last10Average: string;
  seasonAverage: string;
  hitRateVsLine: string;
  bestRecentResult: string;
  sampleWarning?: string;
  trendContext: string;
  lineContext: string;
  log: PlayerResearchLogEntry[];
}

export interface PlayerResearchProfile {
  id: string;
  name: string;
  sport: Exclude<SportCode, 'ALL'>;
  teamOrLeague: string;
  position?: string;
  nextEvent: string;
  opponentContext: string;
  currentLines: MarketOpportunity[];
  stats: PlayerResearchStat[];
}

export const mockPlayerResearchPlayers: PlayerResearchProfile[] = [
  {
    id: 'spencer-strider',
    name: 'Spencer Strider',
    sport: 'MLB',
    teamOrLeague: 'Atlanta Braves',
    position: 'Pitcher',
    nextEvent: 'Braves @ Phillies - Today 7:05 PM',
    opponentContext: 'Projected start vs Philadelphia',
    currentLines: [
      marketLine({
        id: 'player-research-strider-k',
        player: 'Spencer Strider',
        team: 'Braves',
        opponent: 'Phillies',
        event: 'Braves @ Phillies',
        startTime: 'Today 7:05 PM',
        sport: 'MLB',
        propType: 'Pitcher Strikeouts',
        side: 'Over',
        line: 6.5,
        provider: 'Underdog',
        group: 'Underdog',
        american: -120,
        decimal: 1.833,
        model: 0.626,
        implied: 0.545,
        ev: 0.147,
        edge: 0.081,
        confidence: 'Medium',
        modifier: 'Demon',
        sample: 94,
        reasons: ['Pitcher strikeouts have favorable early validation.'],
        warnings: ['Demon payout may vary by provider']
      })
    ],
    stats: [
      stat('pitcher-strikeouts', 'Pitcher Strikeouts', '6.5', '7.4', '7.1', '6.8', '62%', '11 strikeouts vs NYM', 'Limited history after recent workload change.', 'Current strikeout line sits slightly below recent form.', 'Strikeout volume has stayed stable across recent starts.', [
        log('Jun 14', 'vs NYM', '11', '6.5', 'Cleared'),
        log('Jun 8', '@ WSH', '8', '6.5', 'Cleared'),
        log('Jun 2', 'vs ARI', '5', '6.5', 'Missed'),
        log('May 27', '@ PHI', '7', '6.5', 'Cleared'),
        log('May 21', 'vs SD', '6', '6.5', 'Missed')
      ]),
      stat('earned-runs', 'Earned Runs', '2.5', '2.0', '2.3', '2.6', '58%', '1 ER vs NYM', undefined, 'Run prevention trend supports checking alternate pitcher markets.', 'Earned-run outcomes are more matchup sensitive than strikeouts.', [
        log('Jun 14', 'vs NYM', '1', '2.5', 'Cleared'),
        log('Jun 8', '@ WSH', '2', '2.5', 'Cleared'),
        log('Jun 2', 'vs ARI', '4', '2.5', 'Missed'),
        log('May 27', '@ PHI', '3', '2.5', 'Missed'),
        log('May 21', 'vs SD', '0', '2.5', 'Cleared')
      ]),
      stat('outs-recorded', 'Outs Recorded', '17.5', '17.8', '17.3', '16.9', '54%', '21 outs vs NYM', undefined, 'Outs line is close to recent workload.', 'Recent pitch counts have been usable but not aggressive.', [
        log('Jun 14', 'vs NYM', '21', '17.5', 'Cleared'),
        log('Jun 8', '@ WSH', '18', '17.5', 'Cleared'),
        log('Jun 2', 'vs ARI', '15', '17.5', 'Missed'),
        log('May 27', '@ PHI', '16', '17.5', 'Missed'),
        log('May 21', 'vs SD', '19', '17.5', 'Cleared')
      ]),
      stat('walks', 'Walks', '2.5', '2.2', '2.0', '2.1', '44%', '4 walks vs ARI', 'Walk props are thin in this sample.', 'Walk line needs matchup confirmation.', 'Recent walk count is mixed and less stable than strikeouts.', [
        log('Jun 14', 'vs NYM', '1', '2.5', 'Missed'),
        log('Jun 8', '@ WSH', '2', '2.5', 'Missed'),
        log('Jun 2', 'vs ARI', '4', '2.5', 'Cleared'),
        log('May 27', '@ PHI', '3', '2.5', 'Cleared'),
        log('May 21', 'vs SD', '1', '2.5', 'Missed')
      ])
    ]
  },
  {
    id: 'mookie-betts',
    name: 'Mookie Betts',
    sport: 'MLB',
    teamOrLeague: 'Los Angeles Dodgers',
    position: 'Hitter',
    nextEvent: 'Dodgers @ Giants - Today 9:10 PM',
    opponentContext: 'Projected leadoff spot',
    currentLines: [
      marketLine({
        id: 'player-research-betts-total-bases',
        player: 'Mookie Betts',
        team: 'Dodgers',
        opponent: 'Giants',
        event: 'Dodgers @ Giants',
        startTime: 'Today 9:10 PM',
        sport: 'MLB',
        propType: 'Total Bases',
        side: 'Over',
        line: 1.5,
        provider: 'PrizePicks',
        group: 'PrizePicks',
        american: -135,
        decimal: 1.741,
        model: 0.642,
        implied: 0.574,
        ev: 0.118,
        edge: 0.068,
        confidence: 'Strong',
        modifier: 'Normal',
        sample: 128,
        reasons: ['Active DFS row with positive model edge.'],
        warnings: []
      }),
      marketLine({
        id: 'player-research-betts-total-bases-underdog',
        player: 'Mookie Betts',
        team: 'Dodgers',
        opponent: 'Giants',
        event: 'Dodgers @ Giants',
        startTime: 'Today 9:10 PM',
        sport: 'MLB',
        propType: 'Total Bases',
        side: 'Over',
        line: 2,
        provider: 'Underdog',
        group: 'Underdog',
        american: -110,
        decimal: 1.909,
        model: 0.534,
        implied: 0.524,
        ev: 0.021,
        edge: 0.01,
        confidence: 'Medium',
        modifier: 'Normal',
        sample: 128,
        reasons: ['Higher alternate total-bases line.'],
        warnings: []
      }),
      marketLine({
        id: 'player-research-betts-hits-pick6',
        player: 'Mookie Betts',
        team: 'Dodgers',
        opponent: 'Giants',
        event: 'Dodgers @ Giants',
        startTime: 'Today 9:10 PM',
        sport: 'MLB',
        propType: 'Hits',
        side: 'Over',
        line: 0.5,
        provider: 'DraftKings Pick6',
        group: 'DraftKings Pick6',
        american: -145,
        decimal: 1.69,
        model: 0.61,
        implied: 0.592,
        ev: 0.031,
        edge: 0.018,
        confidence: 'Medium',
        modifier: 'Discount',
        sample: 128,
        reasons: ['Lower hit line with standard entry eligibility.'],
        warnings: []
      }),
      marketLine({
        id: 'player-research-betts-runs-prizepicks',
        player: 'Mookie Betts',
        team: 'Dodgers',
        opponent: 'Giants',
        event: 'Dodgers @ Giants',
        startTime: 'Today 9:10 PM',
        sport: 'MLB',
        propType: 'Runs',
        side: 'Over',
        line: 0.5,
        provider: 'PrizePicks',
        group: 'PrizePicks',
        american: -105,
        decimal: 1.952,
        model: 0.57,
        implied: 0.512,
        ev: 0.074,
        edge: 0.058,
        confidence: 'Medium',
        modifier: 'Normal',
        sample: 96,
        reasons: ['Leadoff role keeps run chances active.'],
        warnings: []
      }),
      marketLine({
        id: 'player-research-betts-hits-underdog',
        player: 'Mookie Betts',
        team: 'Dodgers',
        opponent: 'Giants',
        event: 'Dodgers @ Giants',
        startTime: 'Today 9:10 PM',
        sport: 'MLB',
        propType: 'Hits',
        side: 'Over',
        line: 1.5,
        provider: 'Underdog',
        group: 'Underdog',
        american: 105,
        decimal: 2.05,
        model: 0.482,
        implied: 0.488,
        ev: -0.012,
        edge: -0.006,
        confidence: 'Weak',
        modifier: 'Normal',
        sample: 128,
        reasons: ['Multi-hit line is tighter than the lower hit offering.'],
        warnings: ['Limited history']
      }),
      marketLine({
        id: 'player-research-betts-runs-betr',
        player: 'Mookie Betts',
        team: 'Dodgers',
        opponent: 'Giants',
        event: 'Dodgers @ Giants',
        startTime: 'Today 9:10 PM',
        sport: 'MLB',
        propType: 'Runs',
        side: 'Over',
        line: 1.5,
        provider: 'Betr Picks',
        group: 'Betr Picks',
        american: 118,
        decimal: 2.18,
        model: 0.43,
        implied: 0.459,
        ev: -0.063,
        edge: -0.029,
        confidence: 'Weak',
        modifier: 'Payout Boost',
        sample: 96,
        reasons: ['Alternate run line has payout context.'],
        warnings: ['Payout may vary']
      }),
      marketLine({
        id: 'player-research-betts-total-bases-pick6',
        player: 'Mookie Betts',
        team: 'Dodgers',
        opponent: 'Giants',
        event: 'Dodgers @ Giants',
        startTime: 'Today 9:10 PM',
        sport: 'MLB',
        propType: 'Total Bases',
        side: 'Under',
        line: 2.5,
        provider: 'DraftKings Pick6',
        group: 'DraftKings Pick6',
        american: -112,
        decimal: 1.893,
        model: 0.548,
        implied: 0.528,
        ev: 0.038,
        edge: 0.02,
        confidence: 'Medium',
        modifier: 'Normal',
        sample: 128,
        reasons: ['Alternate total-bases side for line comparison.'],
        warnings: []
      }),
      marketLine({
        id: 'player-research-betts-hits-prizepicks',
        player: 'Mookie Betts',
        team: 'Dodgers',
        opponent: 'Giants',
        event: 'Dodgers @ Giants',
        startTime: 'Today 9:10 PM',
        sport: 'MLB',
        propType: 'Hits',
        side: 'Over',
        line: 0.5,
        provider: 'PrizePicks',
        group: 'PrizePicks',
        american: -155,
        decimal: 1.645,
        model: 0.624,
        implied: 0.608,
        ev: 0.027,
        edge: 0.016,
        confidence: 'Medium',
        modifier: 'Normal',
        sample: 128,
        reasons: ['Lower hits line from another DFS provider.'],
        warnings: []
      }),
      ...bettsExtraCurrentLines()
    ],
    stats: [
      stat('total-bases', 'Total Bases', '1.5', '2.6', '2.2', '2.0', '64%', '6 total bases vs COL', undefined, 'Current line is within his common DFS range.', 'Extra-base contact has supported the total-bases profile recently.', [
        log('Jun 15', 'vs COL', '6', '1.5', 'Cleared'),
        log('Jun 13', '@ SD', '2', '1.5', 'Cleared'),
        log('Jun 12', '@ SD', '1', '1.5', 'Missed'),
        log('Jun 10', 'vs SF', '3', '1.5', 'Cleared'),
        log('Jun 9', 'vs SF', '1', '1.5', 'Missed')
      ]),
      stat('hits', 'Hits', '1.5', '1.4', '1.2', '1.1', '48%', '3 hits vs COL', undefined, 'Hits line is tighter than total bases.', 'Contact has been steady, but multi-hit clears remain volatile.', [
        log('Jun 15', 'vs COL', '3', '1.5', 'Cleared'),
        log('Jun 13', '@ SD', '1', '1.5', 'Missed'),
        log('Jun 12', '@ SD', '1', '1.5', 'Missed'),
        log('Jun 10', 'vs SF', '2', '1.5', 'Cleared'),
        log('Jun 9', 'vs SF', '0', '1.5', 'Missed')
      ]),
      stat('runs', 'Runs', '0.5', '0.8', '0.7', '0.7', '59%', '2 runs vs COL', undefined, 'Run scoring leans on lineup context.', 'Leadoff role keeps run chances active.', [
        log('Jun 15', 'vs COL', '2', '0.5', 'Cleared'),
        log('Jun 13', '@ SD', '1', '0.5', 'Cleared'),
        log('Jun 12', '@ SD', '0', '0.5', 'Missed'),
        log('Jun 10', 'vs SF', '1', '0.5', 'Cleared'),
        log('Jun 9', 'vs SF', '0', '0.5', 'Missed')
      ])
    ]
  },
  {
    id: 'jayson-tatum',
    name: 'Jayson Tatum',
    sport: 'NBA',
    teamOrLeague: 'Boston Celtics',
    position: 'Forward',
    nextEvent: 'Celtics @ Heat - Tomorrow 8:00 PM',
    opponentContext: 'High-usage matchup',
    currentLines: [
      marketLine({
        id: 'player-research-tatum-pr',
        player: 'Jayson Tatum',
        team: 'Celtics',
        opponent: 'Heat',
        event: 'Celtics @ Heat',
        startTime: 'Tomorrow 8:00 PM',
        sport: 'NBA',
        propType: 'Points + Rebounds',
        side: 'Under',
        line: 38.5,
        provider: 'DraftKings Pick6',
        group: 'DraftKings Pick6',
        american: -115,
        decimal: 1.87,
        model: 0.602,
        implied: 0.535,
        ev: 0.126,
        edge: 0.067,
        confidence: 'Medium',
        modifier: 'Discount',
        sample: 68,
        reasons: ['Discounted line context with positive edge.'],
        warnings: ['Limited history']
      }),
      marketLine({
        id: 'player-research-tatum-pr-prizepicks',
        player: 'Jayson Tatum',
        team: 'Celtics',
        opponent: 'Heat',
        event: 'Celtics @ Heat',
        startTime: 'Tomorrow 8:00 PM',
        sport: 'NBA',
        propType: 'Points + Rebounds',
        side: 'Over',
        line: 39.5,
        provider: 'PrizePicks',
        group: 'PrizePicks',
        american: -125,
        decimal: 1.8,
        model: 0.558,
        implied: 0.556,
        ev: 0.044,
        edge: 0.002,
        confidence: 'Medium',
        modifier: 'Normal',
        sample: 68,
        reasons: ['Alternate provider line for the same points plus rebounds market.'],
        warnings: []
      }),
      marketLine({
        id: 'player-research-tatum-pr-underdog',
        player: 'Jayson Tatum',
        team: 'Celtics',
        opponent: 'Heat',
        event: 'Celtics @ Heat',
        startTime: 'Tomorrow 8:00 PM',
        sport: 'NBA',
        propType: 'Points + Rebounds',
        side: 'Over',
        line: 40,
        provider: 'Underdog',
        group: 'Underdog',
        american: -110,
        decimal: 1.909,
        model: 0.542,
        implied: 0.524,
        ev: 0.035,
        edge: 0.018,
        confidence: 'Weak',
        modifier: 'Normal',
        sample: 68,
        reasons: ['Higher provider line with less clearance room.'],
        warnings: ['Payout may vary']
      }),
      marketLine({
        id: 'player-research-tatum-points-underdog',
        player: 'Jayson Tatum',
        team: 'Celtics',
        opponent: 'Heat',
        event: 'Celtics @ Heat',
        startTime: 'Tomorrow 8:00 PM',
        sport: 'NBA',
        propType: 'Points',
        side: 'Over',
        line: 27.5,
        provider: 'Underdog',
        group: 'Underdog',
        american: -112,
        decimal: 1.893,
        model: 0.57,
        implied: 0.528,
        ev: 0.079,
        edge: 0.042,
        confidence: 'Medium',
        modifier: 'Normal',
        sample: 72,
        reasons: ['Points line tracks recent shot volume.'],
        warnings: []
      }),
      marketLine({
        id: 'player-research-tatum-threes-prizepicks',
        player: 'Jayson Tatum',
        team: 'Celtics',
        opponent: 'Heat',
        event: 'Celtics @ Heat',
        startTime: 'Tomorrow 8:00 PM',
        sport: 'NBA',
        propType: 'Threes Made',
        side: 'Over',
        line: 2.5,
        provider: 'PrizePicks',
        group: 'PrizePicks',
        american: -105,
        decimal: 1.952,
        model: 0.548,
        implied: 0.512,
        ev: 0.07,
        edge: 0.036,
        confidence: 'Weak',
        modifier: 'Normal',
        sample: 55,
        reasons: ['Lower threes line than the standard comparison row.'],
        warnings: []
      }),
      marketLine({
        id: 'player-research-tatum-points-prizepicks',
        player: 'Jayson Tatum',
        team: 'Celtics',
        opponent: 'Heat',
        event: 'Celtics @ Heat',
        startTime: 'Tomorrow 8:00 PM',
        sport: 'NBA',
        propType: 'Points',
        side: 'Over',
        line: 28.5,
        provider: 'PrizePicks',
        group: 'PrizePicks',
        american: -110,
        decimal: 1.909,
        model: 0.536,
        implied: 0.524,
        ev: 0.023,
        edge: 0.012,
        confidence: 'Medium',
        modifier: 'Normal',
        sample: 72,
        reasons: ['Slightly higher points line with standard payout context.'],
        warnings: []
      }),
      marketLine({
        id: 'player-research-tatum-threes-pick6',
        player: 'Jayson Tatum',
        team: 'Celtics',
        opponent: 'Heat',
        event: 'Celtics @ Heat',
        startTime: 'Tomorrow 8:00 PM',
        sport: 'NBA',
        propType: 'Threes Made',
        side: 'Over',
        line: 3.5,
        provider: 'DraftKings Pick6',
        group: 'DraftKings Pick6',
        american: 102,
        decimal: 2.02,
        model: 0.46,
        implied: 0.495,
        ev: -0.071,
        edge: -0.035,
        confidence: 'Weak',
        modifier: 'Discount',
        sample: 55,
        reasons: ['Higher threes line needs attempt context.'],
        warnings: ['Limited history']
      }),
      marketLine({
        id: 'player-research-tatum-points-betr',
        player: 'Jayson Tatum',
        team: 'Celtics',
        opponent: 'Heat',
        event: 'Celtics @ Heat',
        startTime: 'Tomorrow 8:00 PM',
        sport: 'NBA',
        propType: 'Points',
        side: 'Under',
        line: 31.5,
        provider: 'Betr Picks',
        group: 'Betr Picks',
        american: -108,
        decimal: 1.926,
        model: 0.552,
        implied: 0.519,
        ev: 0.064,
        edge: 0.033,
        confidence: 'Medium',
        modifier: 'Payout Boost',
        sample: 72,
        reasons: ['Alternate points side with provider payout context.'],
        warnings: ['Payout may vary']
      })
    ],
    stats: [
      stat('points-rebounds', 'Points + Rebounds', '38.5', '37.8', '39.1', '38.7', '53%', '47 PR vs CLE', 'NBA sample is thin for this market.', 'Current combo line is close to season form.', 'Recent game flow has created mixed PR outcomes.', [
        log('Jun 12', 'vs CLE', '47', '38.5', 'Missed'),
        log('Jun 9', '@ MIL', '36', '38.5', 'Cleared'),
        log('Jun 7', 'vs NYK', '34', '38.5', 'Cleared'),
        log('Jun 4', '@ MIA', '41', '38.5', 'Missed'),
        log('Jun 1', 'vs IND', '31', '38.5', 'Cleared')
      ], 'Under'),
      stat('points', 'Points', '27.5', '28.4', '27.9', '27.1', '55%', '35 points vs CLE', undefined, 'Points line is centered near recent scoring.', 'Shot volume remains high, but efficiency has varied.', [
        log('Jun 12', 'vs CLE', '35', '27.5', 'Cleared'),
        log('Jun 9', '@ MIL', '25', '27.5', 'Missed'),
        log('Jun 7', 'vs NYK', '28', '27.5', 'Cleared'),
        log('Jun 4', '@ MIA', '30', '27.5', 'Cleared'),
        log('Jun 1', 'vs IND', '24', '27.5', 'Missed')
      ]),
      stat('threes', 'Threes Made', '3.5', '3.0', '3.2', '3.1', '46%', '5 threes vs CLE', undefined, 'Three-point line needs matchup and attempt context.', 'Attempts are there, but clears are less consistent.', [
        log('Jun 12', 'vs CLE', '5', '3.5', 'Cleared'),
        log('Jun 9', '@ MIL', '3', '3.5', 'Missed'),
        log('Jun 7', 'vs NYK', '2', '3.5', 'Missed'),
        log('Jun 4', '@ MIA', '4', '3.5', 'Cleared'),
        log('Jun 1', 'vs IND', '1', '3.5', 'Missed')
      ])
    ]
  },
  {
    id: 'rory-mcilroy',
    name: 'Rory McIlroy',
    sport: 'GOLF',
    teamOrLeague: 'PGA Tour',
    position: 'Golfer',
    nextEvent: 'Travelers Championship - Thursday 1:20 PM',
    opponentContext: 'Round 1 tee time pending',
    currentLines: [
      marketLine({
        id: 'player-research-rory-birdies',
        player: 'Rory McIlroy',
        event: 'Travelers Championship',
        startTime: 'Thursday 1:20 PM',
        sport: 'GOLF',
        propType: 'Birdies',
        side: 'Over',
        line: 4.5,
        provider: 'PrizePicks',
        group: 'PrizePicks',
        american: -105,
        decimal: 1.952,
        model: 0.548,
        implied: 0.512,
        ev: 0.071,
        edge: 0.036,
        confidence: 'Weak',
        modifier: 'Bonus Lineup',
        sample: 31,
        reasons: ['Positive edge with bonus-lineup modifier.'],
        warnings: ['Payout may vary']
      })
    ],
    stats: [
      stat('birdies', 'Birdies', '4.5', '4.8', '4.4', '4.2', '51%', '7 birdies in Round 2', 'Limited history for this event setup.', 'Birdie line is slightly above season average.', 'Recent scoring chances are positive but course dependent.', [
        log('Jun 14', 'US Open R4', '4', '4.5', 'Missed'),
        log('Jun 13', 'US Open R3', '5', '4.5', 'Cleared'),
        log('Jun 12', 'US Open R2', '7', '4.5', 'Cleared'),
        log('Jun 11', 'US Open R1', '3', '4.5', 'Missed'),
        log('Jun 2', 'Memorial R4', '5', '4.5', 'Cleared')
      ]),
      stat('round-score', 'Round Score', '69.5', '69.2', '69.7', '70.1', '56%', '66 in US Open R2', undefined, 'Round score depends heavily on weather draw.', 'Recent rounds have been slightly better than the displayed line.', [
        log('Jun 14', 'US Open R4', '71', '69.5', 'Missed'),
        log('Jun 13', 'US Open R3', '69', '69.5', 'Cleared'),
        log('Jun 12', 'US Open R2', '66', '69.5', 'Cleared'),
        log('Jun 11', 'US Open R1', '70', '69.5', 'Missed')
      ])
    ]
  },
  {
    id: 'iga-swiatek',
    name: 'Iga Swiatek',
    sport: 'TENNIS',
    teamOrLeague: 'WTA',
    position: 'Singles',
    nextEvent: 'WTA London - Tomorrow 10:00 AM',
    opponentContext: 'Grass-court match',
    currentLines: [
      marketLine({
        id: 'player-research-iga-aces',
        player: 'Iga Swiatek',
        event: 'WTA London',
        startTime: 'Tomorrow 10:00 AM',
        sport: 'TENNIS',
        propType: 'Aces',
        side: 'Over',
        line: 4.5,
        provider: 'Betr Picks',
        group: 'Betr Picks',
        american: -110,
        decimal: 1.909,
        model: 0.57,
        implied: 0.524,
        ev: 0.089,
        edge: 0.046,
        confidence: 'Weak',
        modifier: 'Payout Boost',
        sample: 18,
        reasons: ['Payout boost improves entry shape.'],
        warnings: ['Low sample']
      })
    ],
    stats: [
      stat('aces', 'Aces', '4.5', '4.2', '3.8', '3.4', '42%', '7 aces vs Rybakina', 'Limited history on grass-court ace props.', 'Current line is above season average.', 'Recent ace count has improved, but sample remains light.', [
        log('Jun 15', 'vs Rybakina', '7', '4.5', 'Cleared'),
        log('Jun 12', 'vs Pegula', '4', '4.5', 'Missed'),
        log('Jun 9', 'vs Zheng', '5', '4.5', 'Cleared'),
        log('Jun 6', 'vs Gauff', '2', '4.5', 'Missed'),
        log('Jun 3', 'vs Paolini', '3', '4.5', 'Missed')
      ]),
      stat('games-won', 'Games Won', '12.5', '13.4', '12.9', '12.6', '57%', '17 games vs Rybakina', undefined, 'Games-won line is close to recent match output.', 'Straight-set wins can suppress total games despite strong form.', [
        log('Jun 15', 'vs Rybakina', '17', '12.5', 'Cleared'),
        log('Jun 12', 'vs Pegula', '13', '12.5', 'Cleared'),
        log('Jun 9', 'vs Zheng', '12', '12.5', 'Missed'),
        log('Jun 6', 'vs Gauff', '14', '12.5', 'Cleared')
      ])
    ]
  }
];

function marketLine(input: {
  id: string;
  player: string;
  team?: string;
  opponent?: string;
  event: string;
  startTime: string;
  sport: Exclude<SportCode, 'ALL'>;
  propType: string;
  side: 'Over' | 'Under';
  line: number;
  provider: string;
  group: string;
  american: number;
  decimal: number;
  model: number;
  implied: number;
  ev: number;
  edge: number;
  confidence: 'Strong' | 'Medium' | 'Weak';
  modifier: MarketOpportunity['projectionModifier'];
  sample: number;
  reasons: string[];
  warnings: string[];
}): MarketOpportunity {
  return {
    id: input.id,
    player: input.player,
    team: input.team,
    opponent: input.opponent,
    event: input.event,
    startTime: input.startTime,
    sport: input.sport,
    propType: input.propType,
    side: input.side,
    line: input.line,
    provider: input.provider,
    providerCategory: 'DFS',
    entryCompatibilityGroup: input.group,
    entryProvider: input.provider,
    canAddToEntry: true,
    bestAmericanPrice: input.american,
    bestDecimalPrice: input.decimal,
    modelProbability: input.model,
    impliedProbability: input.implied,
    probabilityEdge: input.edge,
    estimatedEv: input.ev,
    strategyClassification: 'Candidate',
    strategyTier: input.confidence,
    trustLevel: input.confidence,
    marketShape: 'DFS candidate row',
    historicalSampleSize: input.sample,
    freshness: 'Fresh',
    projectionModifier: input.modifier,
    reasons: input.reasons,
    warnings: input.warnings,
    sportsbookContext: 'Reference lines available on comparison board',
    providerLines: [
      { provider: input.provider, providerCategory: 'DFS', line: input.line, note: 'Best DFS line' },
      { provider: 'Reference lines', providerCategory: 'Sportsbook', line: input.line, note: 'Compare price and consensus only' }
    ]
  };
}

function bettsExtraCurrentLines(): MarketOpportunity[] {
  const shared = {
    player: 'Mookie Betts',
    team: 'Dodgers',
    opponent: 'Giants',
    event: 'Dodgers @ Giants',
    startTime: 'Today 9:10 PM',
    sport: 'MLB' as const
  };

  return [
    marketLine({
      ...shared,
      id: 'player-research-betts-total-bases-betr-over',
      propType: 'Total Bases',
      side: 'Over',
      line: 0.5,
      provider: 'Betr Picks',
      group: 'Betr Picks',
      american: -180,
      decimal: 1.556,
      model: 0.704,
      implied: 0.643,
      ev: 0.095,
      edge: 0.061,
      confidence: 'Strong',
      modifier: 'Normal',
      sample: 128,
      reasons: ['Lower total-bases line for inventory testing.'],
      warnings: []
    }),
    marketLine({
      ...shared,
      id: 'player-research-betts-total-bases-prizepicks-under',
      propType: 'Total Bases',
      side: 'Under',
      line: 3.5,
      provider: 'PrizePicks',
      group: 'PrizePicks',
      american: -125,
      decimal: 1.8,
      model: 0.582,
      implied: 0.556,
      ev: 0.048,
      edge: 0.026,
      confidence: 'Medium',
      modifier: 'Normal',
      sample: 128,
      reasons: ['Alternate under line for total bases.'],
      warnings: []
    }),
    marketLine({
      ...shared,
      id: 'player-research-betts-total-bases-underdog-under',
      propType: 'Total Bases',
      side: 'Under',
      line: 2.5,
      provider: 'Underdog',
      group: 'Underdog',
      american: -108,
      decimal: 1.926,
      model: 0.54,
      implied: 0.519,
      ev: 0.04,
      edge: 0.021,
      confidence: 'Medium',
      modifier: 'Normal',
      sample: 128,
      reasons: ['Provider alternate on the same total-bases stat.'],
      warnings: []
    }),
    marketLine({
      ...shared,
      id: 'player-research-betts-total-bases-pick6-over-35',
      propType: 'Total Bases',
      side: 'Over',
      line: 3.5,
      provider: 'DraftKings Pick6',
      group: 'DraftKings Pick6',
      american: 132,
      decimal: 2.32,
      model: 0.392,
      implied: 0.431,
      ev: -0.091,
      edge: -0.039,
      confidence: 'Weak',
      modifier: 'Discount',
      sample: 128,
      reasons: ['Longer alternate total-bases line.'],
      warnings: ['Limited history']
    }),
    marketLine({
      ...shared,
      id: 'player-research-betts-hits-betr-over-05',
      propType: 'Hits',
      side: 'Over',
      line: 0.5,
      provider: 'Betr Picks',
      group: 'Betr Picks',
      american: -150,
      decimal: 1.667,
      model: 0.612,
      implied: 0.6,
      ev: 0.02,
      edge: 0.012,
      confidence: 'Medium',
      modifier: 'Normal',
      sample: 128,
      reasons: ['Another provider listing for lower hits.'],
      warnings: []
    }),
    marketLine({
      ...shared,
      id: 'player-research-betts-hits-pick6-under-15',
      propType: 'Hits',
      side: 'Under',
      line: 1.5,
      provider: 'DraftKings Pick6',
      group: 'DraftKings Pick6',
      american: -135,
      decimal: 1.741,
      model: 0.59,
      implied: 0.574,
      ev: 0.028,
      edge: 0.016,
      confidence: 'Medium',
      modifier: 'Normal',
      sample: 128,
      reasons: ['Under side for multi-hit inventory.'],
      warnings: []
    }),
    marketLine({
      ...shared,
      id: 'player-research-betts-hits-underdog-over-05',
      propType: 'Hits',
      side: 'Over',
      line: 0.5,
      provider: 'Underdog',
      group: 'Underdog',
      american: -160,
      decimal: 1.625,
      model: 0.622,
      implied: 0.615,
      ev: 0.011,
      edge: 0.007,
      confidence: 'Medium',
      modifier: 'Normal',
      sample: 128,
      reasons: ['Provider listing for lower hits.'],
      warnings: []
    }),
    marketLine({
      ...shared,
      id: 'player-research-betts-hits-prizepicks-under-15',
      propType: 'Hits',
      side: 'Under',
      line: 1.5,
      provider: 'PrizePicks',
      group: 'PrizePicks',
      american: -130,
      decimal: 1.769,
      model: 0.572,
      implied: 0.565,
      ev: 0.012,
      edge: 0.007,
      confidence: 'Weak',
      modifier: 'Normal',
      sample: 128,
      reasons: ['Alternate under hits offering.'],
      warnings: []
    }),
    marketLine({
      ...shared,
      id: 'player-research-betts-runs-underdog-over-05',
      propType: 'Runs',
      side: 'Over',
      line: 0.5,
      provider: 'Underdog',
      group: 'Underdog',
      american: -102,
      decimal: 1.98,
      model: 0.552,
      implied: 0.505,
      ev: 0.093,
      edge: 0.047,
      confidence: 'Medium',
      modifier: 'Normal',
      sample: 96,
      reasons: ['Run-scoring line from another provider.'],
      warnings: []
    }),
    marketLine({
      ...shared,
      id: 'player-research-betts-runs-pick6-under-15',
      propType: 'Runs',
      side: 'Under',
      line: 1.5,
      provider: 'DraftKings Pick6',
      group: 'DraftKings Pick6',
      american: -140,
      decimal: 1.714,
      model: 0.64,
      implied: 0.583,
      ev: 0.098,
      edge: 0.057,
      confidence: 'Strong',
      modifier: 'Discount',
      sample: 96,
      reasons: ['Alternate run line with discounted context.'],
      warnings: []
    }),
    marketLine({
      ...shared,
      id: 'player-research-betts-runs-betr-under-05',
      propType: 'Runs',
      side: 'Under',
      line: 0.5,
      provider: 'Betr Picks',
      group: 'Betr Picks',
      american: 112,
      decimal: 2.12,
      model: 0.456,
      implied: 0.472,
      ev: -0.034,
      edge: -0.016,
      confidence: 'Weak',
      modifier: 'Payout Boost',
      sample: 96,
      reasons: ['Lower run under with payout context.'],
      warnings: ['Payout may vary']
    }),
    marketLine({
      ...shared,
      id: 'player-research-betts-runs-prizepicks-under-15',
      propType: 'Runs',
      side: 'Under',
      line: 1.5,
      provider: 'PrizePicks',
      group: 'PrizePicks',
      american: -132,
      decimal: 1.758,
      model: 0.618,
      implied: 0.569,
      ev: 0.086,
      edge: 0.049,
      confidence: 'Medium',
      modifier: 'Normal',
      sample: 96,
      reasons: ['Run under alternate for line inventory.'],
      warnings: []
    })
  ];
}

function stat(
  id: string,
  label: string,
  currentLine: string | undefined,
  last5Average: string,
  last10Average: string,
  seasonAverage: string,
  hitRateVsLine: string,
  bestRecentResult: string,
  sampleWarning: string | undefined,
  lineContext: string,
  trendContext: string,
  log: PlayerResearchLogEntry[],
  side: PlayerResearchStat['side'] = 'Over'
): PlayerResearchStat {
  return {
    id,
    label,
    side,
    currentLine,
    last5Average,
    last10Average,
    seasonAverage,
    hitRateVsLine,
    bestRecentResult,
    sampleWarning,
    lineContext,
    trendContext,
    log
  };
}

function log(date: string, event: string, value: string, line?: string, result?: PlayerResearchLogEntry['result']): PlayerResearchLogEntry {
  return { date, event, value, line, result };
}

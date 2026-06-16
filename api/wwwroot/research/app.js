const endpoints = {
    systemHealth: "/api/debug/system/health",
    errorSummary: "/api/debug/errors/summary",
    recentErrors: "/api/debug/errors/recent?take=5",
    freshness: "/api/debug/markets/freshness-summary",
    rankings: "/api/debug/predictions/market-rankings",
    lineShopping: "/api/debug/the-odds/mlb/line-shopping",
    theOddsEventWorkflow: "/api/debug/the-odds/mlb/event-workflow-status",
    theOddsIngestionPlan: "/api/debug/the-odds/mlb/ingestion-plan",
    theOddsEvents: "/api/debug/the-odds/mlb/player-props-spike",
    theOddsIngestEvent: "/api/debug/the-odds/mlb/ingest-event",
    refreshCurrentMlbRecommendations: "/api/debug/the-odds/mlb/recommendations/refresh-current",
    generateMlbPredictions: "/api/debug/predictions/generate/mlb",
    generateTheOddsMlbPredictions: "/api/debug/predictions/generate/mlb/the-odds",
    evBacktest: "/api/debug/markets/underdog-ev-backtest",
    propReliability: "/api/debug/markets/underdog-prop-ev-reliability"
};

const state = {
    systemHealth: null,
    freshness: null,
    rankings: null,
    lineShopping: null,
    theOddsEventWorkflow: null,
    evBacktest: null,
    propReliability: null,
    errorSummary: null,
    recentErrors: null,
    latestGeneratedRunId: null,
    statusTimerId: null,
    filteredMarkets: [],
    filteredLineShoppingGroups: []
};

const els = {
    status: document.getElementById("status"),
    statusMessage: document.getElementById("statusMessage"),
    statusCloseButton: document.getElementById("statusCloseButton"),
    refreshButton: document.getElementById("refreshButton"),
    totalRankedMarkets: document.getElementById("totalRankedMarkets"),
    visibleMarkets: document.getElementById("visibleMarkets"),
    hiddenMarkets: document.getElementById("hiddenMarkets"),
    staleMarkets: document.getElementById("staleMarkets"),
    eligibleMarkets: document.getElementById("eligibleMarkets"),
    systemHealthBadge: document.getElementById("systemHealthBadge"),
    systemErrorsLast24h: document.getElementById("systemErrorsLast24h"),
    underdogHealth: document.getElementById("underdogHealth"),
    prizePicksHealth: document.getElementById("prizePicksHealth"),
    mlbImportHealth: document.getElementById("mlbImportHealth"),
    predictionResearchHealth: document.getElementById("predictionResearchHealth"),
    systemRecommendedActions: document.getElementById("systemRecommendedActions"),
    appHealthBadge: document.getElementById("appHealthBadge"),
    totalErrors: document.getElementById("totalErrors"),
    errorsLast24h: document.getElementById("errorsLast24h"),
    mostRecentError: document.getElementById("mostRecentError"),
    errorsBySource: document.getElementById("errorsBySource"),
    errorsByComponent: document.getElementById("errorsByComponent"),
    recentErrorRows: document.getElementById("recentErrorRows"),
    copyErrorsButton: document.getElementById("copyErrorsButton"),
    platformFreshness: document.getElementById("platformFreshness"),
    evSummary: document.getElementById("evSummary"),
    propReliabilitySummary: document.getElementById("propReliabilitySummary"),
    propReliabilityRows: document.getElementById("propReliabilityRows"),
    lineShoppingGroups: document.getElementById("lineShoppingGroups"),
    lineShoppingMarkets: document.getElementById("lineShoppingMarkets"),
    lineShoppingBookmakers: document.getElementById("lineShoppingBookmakers"),
    lineShoppingMultiBook: document.getElementById("lineShoppingMultiBook"),
    lineShoppingEligible: document.getElementById("lineShoppingEligible"),
    lineShoppingReady: document.getElementById("lineShoppingReady"),
    topPicksCount: document.getElementById("topPicksCount"),
    topPicksRows: document.getElementById("topPicksRows"),
    lineShoppingCount: document.getElementById("lineShoppingCount"),
    lineShoppingRows: document.getElementById("lineShoppingRows"),
    lineShoppingEligibleOnlyFilter: document.getElementById("lineShoppingEligibleOnlyFilter"),
    lineShoppingMultiBookOnlyFilter: document.getElementById("lineShoppingMultiBookOnlyFilter"),
    lineShoppingPropTypeFilter: document.getElementById("lineShoppingPropTypeFilter"),
    lineShoppingTrustFilter: document.getElementById("lineShoppingTrustFilter"),
    lineShoppingStrategyFilter: document.getElementById("lineShoppingStrategyFilter"),
    lineShoppingPositiveEvOnlyFilter: document.getElementById("lineShoppingPositiveEvOnlyFilter"),
    lineShoppingActiveOnlyFilter: document.getElementById("lineShoppingActiveOnlyFilter"),
    lineShoppingIncludeStaleFilter: document.getElementById("lineShoppingIncludeStaleFilter"),
    lineShoppingIncludeExpiredFilter: document.getElementById("lineShoppingIncludeExpiredFilter"),
    lineShoppingMinRankingFilter: document.getElementById("lineShoppingMinRankingFilter"),
    lineShoppingMinBestEvFilter: document.getElementById("lineShoppingMinBestEvFilter"),
    lineShoppingMinSampleFilter: document.getElementById("lineShoppingMinSampleFilter"),
    lineShoppingMinConfidenceFilter: document.getElementById("lineShoppingMinConfidenceFilter"),
    theOddsWorkflowCount: document.getElementById("theOddsWorkflowCount"),
    theOddsWorkflowTotal: document.getElementById("theOddsWorkflowTotal"),
    theOddsWorkflowLineShoppingReady: document.getElementById("theOddsWorkflowLineShoppingReady"),
    theOddsWorkflowAwaitingResults: document.getElementById("theOddsWorkflowAwaitingResults"),
    theOddsWorkflowCompleted: document.getElementById("theOddsWorkflowCompleted"),
    theOddsWorkflowNeedsAttention: document.getElementById("theOddsWorkflowNeedsAttention"),
    theOddsWorkflowLatestObserved: document.getElementById("theOddsWorkflowLatestObserved"),
    theOddsWorkflowCandidateIngestion: document.getElementById("theOddsWorkflowCandidateIngestion"),
    theOddsWorkflowEstimatedCredits: document.getElementById("theOddsWorkflowEstimatedCredits"),
    theOddsWorkflowRecommendedAction: document.getElementById("theOddsWorkflowRecommendedAction"),
    theOddsWorkflowRows: document.getElementById("theOddsWorkflowRows"),
    theOddsManualStatus: document.getElementById("theOddsManualStatus"),
    fetchTheOddsEventsButton: document.getElementById("fetchTheOddsEventsButton"),
    theOddsEventSelect: document.getElementById("theOddsEventSelect"),
    theOddsEventIdInput: document.getElementById("theOddsEventIdInput"),
    theOddsBookmakersInput: document.getElementById("theOddsBookmakersInput"),
    theOddsMarketsInput: document.getElementById("theOddsMarketsInput"),
    theOddsEventsSummary: document.getElementById("theOddsEventsSummary"),
    theOddsDryRunButton: document.getElementById("theOddsDryRunButton"),
    theOddsCommitConfirm: document.getElementById("theOddsCommitConfirm"),
    theOddsCommitButton: document.getElementById("theOddsCommitButton"),
    theOddsIngestSummary: document.getElementById("theOddsIngestSummary"),
    refreshCurrentMlbDryRunButton: document.getElementById("refreshCurrentMlbDryRunButton"),
    refreshCurrentMlbRecommendationsButton: document.getElementById("refreshCurrentMlbRecommendationsButton"),
    generateMlbPredictionsButton: document.getElementById("generateMlbPredictionsButton"),
    generateFullMlbPredictionsButton: document.getElementById("generateFullMlbPredictionsButton"),
    refreshLineShoppingButton: document.getElementById("refreshLineShoppingButton"),
    theOddsPredictionSummary: document.getElementById("theOddsPredictionSummary"),
    marketRows: document.getElementById("marketRows"),
    tableCount: document.getElementById("tableCount"),
    platformFilter: document.getElementById("platformFilter"),
    classificationFilter: document.getElementById("classificationFilter"),
    propTypeFilter: document.getElementById("propTypeFilter"),
    recommendationFilter: document.getElementById("recommendationFilter"),
    visibleOnlyFilter: document.getElementById("visibleOnlyFilter"),
    eligibleOnlyFilter: document.getElementById("eligibleOnlyFilter"),
    minProbabilityFilter: document.getElementById("minProbabilityFilter"),
    minRankingFilter: document.getElementById("minRankingFilter")
};

document.addEventListener("DOMContentLoaded", () => {
    els.refreshButton.addEventListener("click", loadDashboard);
    els.statusCloseButton.addEventListener("click", dismissStatus);
    els.copyErrorsButton.addEventListener("click", copyRecentErrors);
    document.getElementById("filters").addEventListener("input", applyFilters);
    document.getElementById("lineShoppingFilters").addEventListener("input", applyLineShoppingFilters);
    els.fetchTheOddsEventsButton.addEventListener("click", fetchTheOddsEvents);
    els.theOddsEventSelect.addEventListener("change", selectTheOddsEvent);
    els.theOddsDryRunButton.addEventListener("click", () => ingestTheOddsEvent(true));
    els.theOddsCommitConfirm.addEventListener("change", () => {
        els.theOddsCommitButton.disabled = !els.theOddsCommitConfirm.checked;
    });
    els.theOddsCommitButton.addEventListener("click", () => ingestTheOddsEvent(false));
    els.refreshCurrentMlbDryRunButton.addEventListener("click", () => refreshCurrentMlbRecommendations(true));
    els.refreshCurrentMlbRecommendationsButton.addEventListener("click", () => refreshCurrentMlbRecommendations(false));
    els.generateMlbPredictionsButton.addEventListener("click", () => generateMlbPredictions(true));
    els.generateFullMlbPredictionsButton.addEventListener("click", () => generateMlbPredictions(false));
    els.refreshLineShoppingButton.addEventListener("click", refreshLineShoppingForLatestRun);
    document.querySelectorAll("[data-line-shopping-preset]").forEach(button => {
        button.addEventListener("click", () => applyLineShoppingPreset(button.dataset.lineShoppingPreset));
    });
    document.addEventListener("click", event => {
        const eventRow = event.target.closest(".event-row");
        if (!eventRow) {
            return;
        }

        const eventId = eventRow.getAttribute("data-event-id");
        els.theOddsEventIdInput.value = eventId || "";
        els.theOddsEventSelect.value = eventId || "";
    });
    loadDashboard();
});

async function loadDashboard() {
    setStatus("Loading dashboard data...");

    try {
        const [systemHealth, errorSummary, recentErrors, freshness, rankings, lineShopping, theOddsEventWorkflow, evBacktest, propReliability] = await Promise.all([
            fetchJson(endpoints.systemHealth),
            fetchJson(endpoints.errorSummary),
            fetchJson(endpoints.recentErrors),
            fetchJson(endpoints.freshness),
            fetchJson(endpoints.rankings),
            fetchJson(endpoints.lineShopping),
            fetchJson(endpoints.theOddsIngestionPlan),
            fetchJson(endpoints.evBacktest),
            fetchJson(endpoints.propReliability)
        ]);

        state.systemHealth = systemHealth;
        state.errorSummary = errorSummary;
        state.recentErrors = recentErrors;
        state.freshness = freshness;
        state.rankings = rankings;
        state.lineShopping = lineShopping;
        state.theOddsEventWorkflow = theOddsEventWorkflow;
        state.evBacktest = evBacktest;
        state.propReliability = propReliability;

        renderSystemHealth();
        renderApplicationStatus();
        renderFreshness();
        renderEvSummary();
        renderPropReliability();
        renderTheOddsEventWorkflow();
        renderLineShoppingSummary();
        populateLineShoppingFilters(lineShopping.topGroupsByBestEstimatedEv || []);
        applyLineShoppingFilters();
        populateFilters(rankings.results || []);
        applyFilters();
        setStatus(`Loaded ${formatNumber(rankings.totalRankedMarkets)} ranked markets.`);
    } catch (error) {
        setStatus(`Failed to load dashboard: ${error.message}`, true);
    }
}

async function fetchTheOddsEvents() {
    setTheOddsManualStatus("Fetching events", "warn");
    els.theOddsEventsSummary.innerHTML = metricRow("Status", "Fetching MLB events...");

    try {
        const result = await fetchJson(endpoints.theOddsEvents);
        const events = result.events || [];

        els.theOddsEventSelect.innerHTML = `<option value="">Select event</option>` + events
            .map(event => `<option value="${escapeHtml(event.eventId)}">${escapeHtml(formatEventOption(event))}</option>`)
            .join("");
        if (result.selectedEventId) {
            els.theOddsEventSelect.value = result.selectedEventId;
            els.theOddsEventIdInput.value = result.selectedEventId;
        }

        els.theOddsEventsSummary.innerHTML = [
            metricRow("Selected event", escapeHtml(result.selectedEventId || "-")),
            metricRow("Events returned", formatNumber(events.length)),
            metricRow("Credit used", formatNumber(result.creditUsage?.requestsUsed)),
            metricRow("Credit remaining", formatNumber(result.creditUsage?.requestsRemaining)),
            metricRow("Last request cost", formatNumber(result.creditUsage?.requestsLast)),
            renderEventList(events)
        ].join("");
        setTheOddsManualStatus("Events loaded", "ok");
    } catch (error) {
        els.theOddsEventsSummary.innerHTML = metricRow("Error", escapeHtml(error.message));
        setTheOddsManualStatus("Event fetch failed", "bad");
    }
}

function selectTheOddsEvent() {
    els.theOddsEventIdInput.value = els.theOddsEventSelect.value;
}

async function ingestTheOddsEvent(dryRun) {
    const eventId = els.theOddsEventIdInput.value.trim();
    if (!eventId) {
        setTheOddsManualStatus("Missing event", "bad");
        els.theOddsIngestSummary.innerHTML = metricRow("Error", "Select or enter an event id.");
        return;
    }

    if (!dryRun && !els.theOddsCommitConfirm.checked) {
        setTheOddsManualStatus("Confirm commit", "warn");
        els.theOddsIngestSummary.innerHTML = metricRow("Error", "Confirm credit/write acknowledgement before commit.");
        return;
    }

    setTheOddsManualStatus(dryRun ? "Dry run running" : "Commit running", "warn");
    els.theOddsIngestSummary.innerHTML = metricRow("Status", dryRun ? "Running dry run..." : "Committing event...");

    const payload = {
        eventId,
        bookmakers: els.theOddsBookmakersInput.value.trim(),
        markets: els.theOddsMarketsInput.value.trim()
    };

    try {
        const result = await postJson(`${endpoints.theOddsIngestEvent}?dryRun=${dryRun}`, payload);
        els.theOddsIngestSummary.innerHTML = renderTheOddsIngestResult(result);
        setTheOddsManualStatus(dryRun ? "Dry run complete" : "Commit complete", "ok");
    } catch (error) {
        els.theOddsIngestSummary.innerHTML = metricRow("Error", escapeHtml(error.message));
        setTheOddsManualStatus(dryRun ? "Dry run failed" : "Commit failed", "bad");
    }
}

async function generateMlbPredictions(scopedToTheOdds) {
    const eventId = els.theOddsEventIdInput.value.trim();
    const params = new URLSearchParams();
    if (scopedToTheOdds && eventId) {
        params.set("eventId", eventId);
    }

    if (scopedToTheOdds && els.theOddsBookmakersInput.value.trim()) {
        params.set("bookmakers", els.theOddsBookmakersInput.value.trim());
    }

    if (scopedToTheOdds && els.theOddsMarketsInput.value.trim()) {
        params.set("markets", els.theOddsMarketsInput.value.trim());
    }

    const endpoint = scopedToTheOdds
        ? `${endpoints.generateTheOddsMlbPredictions}${params.toString() ? `?${params}` : ""}`
        : endpoints.generateMlbPredictions;
    const label = scopedToTheOdds ? "The Odds MLB predictions" : "full MLB predictions";

    setTheOddsManualStatus("Generating predictions", "warn");
    els.theOddsPredictionSummary.innerHTML = metricRow("Status", `Generating ${label}...`);

    try {
        const result = await postJson(endpoint, {});
        state.latestGeneratedRunId = result.runId;
        els.theOddsPredictionSummary.innerHTML = [
            metricRow("Run ID", `<code>${escapeHtml(result.runId)}</code>`),
            metricRow("Model", `${escapeHtml(result.modelName)} ${escapeHtml(result.modelVersion)}`),
            metricRow("Scope", escapeHtml(result.scope || (scopedToTheOdds ? "TheOddsApiMlb" : "FullMlb"))),
            metricRow("Props evaluated", formatNumber(result.propsEvaluated)),
            metricRow("Predictions generated", formatNumber(result.predictionsGenerated)),
            metricRow("Skipped props", formatNumber(result.skippedProps)),
            metricRow("Suppressed by threshold", formatNumber(result.recommendationsSuppressedByThreshold)),
            metricRow("Suppressed by prop filter", formatNumber(result.recommendationsSuppressedByPropTypeFilter)),
            metricRow("Duration", `${formatNumber(result.diagnostics?.totalDurationMs)} ms`)
        ].join("");
        setTheOddsManualStatus("Predictions generated", "ok");
    } catch (error) {
        els.theOddsPredictionSummary.innerHTML = metricRow("Error", escapeHtml(error.message));
        setTheOddsManualStatus("Prediction failed", "bad");
    }
}

async function refreshLineShoppingForLatestRun() {
    const runId = state.latestGeneratedRunId;
    const url = runId
        ? `${endpoints.lineShopping}?runId=${encodeURIComponent(runId)}`
        : endpoints.lineShopping;

    setTheOddsManualStatus("Refreshing line shopping", "warn");

    try {
        const result = await fetchJson(url);
        state.lineShopping = result;
        renderLineShoppingSummary();
        populateLineShoppingFilters(result.topGroupsByBestEstimatedEv || []);
        applyLineShoppingFilters();
        setTheOddsManualStatus("Line shopping refreshed", "ok");
        setStatus(`Line shopping refreshed for ${runId || "latest run"}.`);
    } catch (error) {
        setTheOddsManualStatus("Refresh failed", "bad");
        setStatus(`Line shopping refresh failed: ${error.message}`, true);
    }
}

function renderTheOddsIngestResult(result) {
    const warnings = result.warnings || [];
    const errors = result.errors || [];

    return [
        metricRow("Success", result.success ? "true" : "false"),
        metricRow("Dry run", result.dryRun ? "true" : "false"),
        metricRow("Event", `${escapeHtml(result.awayTeam || "-")} @ ${escapeHtml(result.homeTeam || "-")}`),
        metricRow("Normalized records", formatNumber(result.normalizedRecords)),
        metricRow("Props upserted", formatNumber(result.propsUpserted)),
        metricRow("Platform props upserted", formatNumber(result.platformPropsUpserted)),
        metricRow("Snapshots created", formatNumber(result.snapshotsCreated)),
        metricRow("Players resolved", formatNumber(result.playersResolved)),
        metricRow("Players created", formatNumber(result.playersCreated)),
        metricRow("Credit used", formatNumber(result.creditUsage?.requestsUsed)),
        metricRow("Credit remaining", formatNumber(result.creditUsage?.requestsRemaining)),
        metricRow("Warnings", warnings.length === 0 ? "-" : formatList(warnings)),
        metricRow("Errors", errors.length === 0 ? "-" : formatList(errors))
    ].join("");
}

async function refreshCurrentMlbRecommendations(dryRun) {
    const params = new URLSearchParams();
    params.set("dryRun", dryRun ? "true" : "false");
    params.set("includeCandidates", "true");
    params.set("freshnessMinutes", "30");

    setTheOddsManualStatus(dryRun ? "Planning refresh" : "Refreshing current slate", "warn");
    els.theOddsPredictionSummary.innerHTML = metricRow(
        "Status",
        dryRun
            ? "Planning current MLB recommendation refresh without API calls or writes..."
            : "Fetching current The Odds API slate, ingesting markets, generating predictions, and rebuilding line shopping...");

    try {
        const result = await postJson(`${endpoints.refreshCurrentMlbRecommendations}?${params}`, {});
        els.theOddsPredictionSummary.innerHTML = renderCurrentRecommendationRefreshResult(result);

        if (!dryRun && result.predictionRunId) {
            state.latestGeneratedRunId = result.predictionRunId;
            await refreshLineShoppingForLatestRun();
            const hasActiveCandidates = (result.activeCandidateCount || 0) > 0;
            if (hasActiveCandidates) {
                applyLineShoppingPreset("conservativeCandidates");
            }
        }

        if (!dryRun && (result.lineShoppingSummary?.activeGroups || 0) === 0) {
            setStatus("Current refresh completed, but all returned line-shopping rows are stale, expired, or game-started.", true);
        } else {
            setStatus(dryRun ? "Current recommendation refresh dry-run complete." : "Current MLB recommendations refreshed.");
        }

        setTheOddsManualStatus(dryRun ? "Dry run complete" : "Current refresh complete", "ok");
    } catch (error) {
        els.theOddsPredictionSummary.innerHTML = metricRow("Error", escapeHtml(error.message));
        setTheOddsManualStatus(dryRun ? "Dry run failed" : "Current refresh failed", "bad");
        setStatus(`Current MLB recommendation refresh failed: ${error.message}`, true);
    }
}

function renderCurrentRecommendationRefreshResult(result) {
    const diagnostics = result.diagnostics || {};
    const lineShopping = result.lineShoppingSummary || {};
    const summaryRows = [
        metricRow("Success", result.success ? "true" : "false"),
        metricRow("Dry run", result.dryRun ? "true" : "false"),
        metricRow("Slate date", escapeHtml(result.slateDate || "-")),
        metricRow("Run ID", result.predictionRunId ? `<code>${escapeHtml(result.predictionRunId)}</code>` : "-"),
        metricRow("Odds events fetched", formatNumber(result.oddsEventsFetched)),
        metricRow("API calls", formatNumber(diagnostics.apiCallsMade)),
        metricRow("Platform props created", formatNumber(result.platformPropsCreated)),
        metricRow("Platform props updated", formatNumber(result.platformPropsUpdated)),
        metricRow("Predictions", formatNumber(result.predictionCount)),
        metricRow("Line-shopping groups", formatNumber(lineShopping.totalComparisonGroups)),
        metricRow("Active groups", formatNumber(lineShopping.activeGroups)),
        metricRow("Active candidates", formatNumber(result.activeCandidateCount)),
        metricRow("Active watchlist", formatNumber(result.activeWatchlistCount)),
        metricRow("Active avoid", formatNumber(result.activeAvoidCount))
    ];

    if (diagnostics.warning) {
        summaryRows.push(metricRow("Warning", escapeHtml(diagnostics.warning)));
    }

    if (result.dryRun) {
        summaryRows.push(metricRow("Dry-run behavior", "No The Odds API calls and no database writes."));
        summaryRows.push(metricRow("Estimated calls", escapeHtml(diagnostics.estimatedApiCalls || "-")));
    }

    return summaryRows.join("");
}

function renderEventList(events) {
    if (!events || events.length === 0) {
        return `<div class="metric-row"><span>Events</span><strong>-</strong></div>`;
    }

    return `
        <div class="event-list">
            ${events.slice(0, 12).map(event => `
                <button class="event-row" type="button" data-event-id="${escapeHtml(event.eventId)}">
                    <strong>${escapeHtml(event.awayTeam || "-")} @ ${escapeHtml(event.homeTeam || "-")}</strong>
                    <span>${escapeHtml(formatDate(event.eventStartTime))}</span>
                    <code>${escapeHtml(event.eventId)}</code>
                </button>
            `).join("")}
        </div>
    `;
}

function formatEventOption(event) {
    return `${event.awayTeam || "-"} @ ${event.homeTeam || "-"} (${formatDate(event.eventStartTime)})`;
}

function setTheOddsManualStatus(label, tone) {
    els.theOddsManualStatus.className = `chip ${tone}`;
    els.theOddsManualStatus.textContent = label;
}

function renderLineShoppingSummary() {
    const summary = state.lineShopping?.summary || {};
    const ready = state.lineShopping?.findings?.appearsReadyForDashboardDisplay;

    els.lineShoppingGroups.textContent = formatNumber(summary.totalComparisonGroups);
    els.lineShoppingMarkets.textContent = formatNumber(summary.totalMarketsCompared);
    els.lineShoppingBookmakers.textContent = formatNumber(summary.totalBookmakers);
    els.lineShoppingMultiBook.textContent = formatNumber(summary.groupsWithMultipleBookmakers);
    els.lineShoppingEligible.textContent = formatNumber(summary.eligibleComparisonGroups);
    els.lineShoppingReady.innerHTML = ready ? chip("Ready", "ok") : chip("Research", "warn");
    renderTopPicks();
}

function renderTheOddsEventWorkflow() {
    const summary = state.theOddsEventWorkflow?.summary || {};
    const events = state.theOddsEventWorkflow?.events || [];

    els.theOddsWorkflowCount.textContent = `${formatNumber(events.length)} events`;
    els.theOddsWorkflowTotal.textContent = formatNumber(summary.totalPersistedEvents);
    els.theOddsWorkflowLineShoppingReady.textContent = formatNumber(summary.eventsInLineShopping);
    els.theOddsWorkflowAwaitingResults.textContent = formatNumber(summary.eventsAwaitingResults);
    els.theOddsWorkflowCompleted.textContent = formatNumber(summary.eventsCompleted);
    els.theOddsWorkflowNeedsAttention.textContent = formatNumber(summary.eventsNeedingAttention);
    els.theOddsWorkflowLatestObserved.textContent = summary.recommendedNextAction || "-";
    els.theOddsWorkflowCandidateIngestion.textContent = formatNumber(summary.candidateEventsForManualIngestion);
    els.theOddsWorkflowEstimatedCredits.textContent = formatNumber(summary.estimatedCreditsIfIngestingAllCandidateEvents);
    els.theOddsWorkflowRecommendedAction.textContent = summary.recommendedNextAction || "-";

    els.theOddsWorkflowRows.innerHTML = events.length === 0
        ? `<tr><td colspan="14" class="muted">No persisted The Odds API MLB events yet.</td></tr>`
        : events.map(event => `
            <tr>
                <td>${chip(event.status, workflowStatusChip(event.status))}</td>
                <td>
                    <strong>${escapeHtml(event.awayTeam || "-")} @ ${escapeHtml(event.homeTeam || "-")}</strong>
                    <br><code>${escapeHtml(event.eventId)}</code>
                </td>
                <td>${formatDate(event.eventStartTime)}</td>
                <td>${event.coverageOpportunity ? chip("Opportunity", "warn") : chip("Covered", "ok")}</td>
                <td>${chip(event.attentionSeverity || "None", statusChip(event.attentionSeverity || "Healthy"))}</td>
                <td>${event.needsPrediction ? chip("Yes", "warn") : chip("No", "ok")}</td>
                <td>${formatNumber(event.platformPropCount)}</td>
                <td>${formatNumber(event.latestRunPredictionCount ?? event.predictionCount)}</td>
                <td>${formatNumber(event.lineShoppingGroupCount)}</td>
                <td>${event.hasResults ? chip("Yes", "ok") : chip("No", "warn")}</td>
                <td>${formatNumber(event.backtestGradedGroups)} / ${formatNumber(event.backtestTotalGroups)}</td>
                <td>${formatNumber(event.missingMappingCount)}</td>
                <td>${formatNumber(event.missingHistoricalResultsCount)}</td>
                <td>${(event.reasons || []).map(reason => `<div>${escapeHtml(reason)}</div>`).join("") || `<span class="muted">-</span>`}</td>
            </tr>
        `).join("");
}

function renderSystemHealth() {
    const health = state.systemHealth || {};
    const overall = health.overall || {};
    const sportsbooks = health.sportsbookIngestionHealth || [];
    const underdog = sportsbooks.find(row => row.platform === "Underdog Fantasy") || {};
    const prizePicks = sportsbooks.find(row => row.platform === "PrizePicks") || {};
    const mlbImport = health.mlbResultImportHealth || {};
    const prediction = health.predictionResearchHealth || {};
    const status = overall.status || "Unknown";

    els.systemHealthBadge.className = `chip ${statusChip(status)}`;
    els.systemHealthBadge.textContent = status;
    els.systemErrorsLast24h.textContent = formatNumber(overall.applicationErrorsLast24h);
    els.underdogHealth.innerHTML = chip(underdog.status || "Unknown", statusChip(underdog.status));
    els.prizePicksHealth.innerHTML = chip(prizePicks.status || "Unknown", statusChip(prizePicks.status));
    els.mlbImportHealth.innerHTML = chip(mlbImport.status || "Unknown", statusChip(mlbImport.status));
    els.predictionResearchHealth.innerHTML = chip(prediction.evResearchStatus || "Unknown", statusChip(prediction.evResearchStatus));

    const actions = [
        ["Underdog", underdog.recommendedAction],
        ["PrizePicks", prizePicks.recommendedAction],
        ["MLB import", mlbImport.recommendedAction],
        ["Prediction research", prediction.recommendedAction]
    ].filter(([, action]) => action);

    els.systemRecommendedActions.innerHTML = actions.length === 0
        ? `<div class="metric-row"><span class="muted">Recommended actions</span><strong>-</strong></div>`
        : actions.map(([label, action]) => metricRow(label, escapeHtml(action))).join("");
}

function renderApplicationStatus() {
    const summary = state.errorSummary || {};
    const recentErrors = state.recentErrors?.errors || [];
    const hasErrorSeverity = recentErrors.some(error => String(error.severity || "").toLowerCase() === "error");
    const errorsLast24h = summary.errorsLast24h || 0;
    const healthLabel = hasErrorSeverity
        ? "Error"
        : errorsLast24h > 0
            ? "Warning"
            : "Healthy";
    const healthClass = healthLabel === "Healthy"
        ? "ok"
        : healthLabel === "Warning"
            ? "warn"
            : "bad";

    els.appHealthBadge.className = `chip ${healthClass}`;
    els.appHealthBadge.textContent = healthLabel;
    els.totalErrors.textContent = formatNumber(summary.totalErrors);
    els.errorsLast24h.textContent = formatNumber(errorsLast24h);
    els.mostRecentError.textContent = formatDate(summary.mostRecentErrorTimestamp);
    els.errorsBySource.innerHTML = renderBreakdown(summary.errorsBySource, "source");
    els.errorsByComponent.innerHTML = renderBreakdown(summary.errorsByComponent, "component");
    els.recentErrorRows.innerHTML = recentErrors.length === 0
        ? `<tr><td colspan="7" class="muted">No recent errors.</td></tr>`
        : recentErrors.map(error => `
            <tr>
                <td>${formatDate(error.occurredAtUtc)}</td>
                <td>${chip(error.severity, String(error.severity).toLowerCase() === "error" ? "bad" : "warn")}</td>
                <td>${escapeHtml(error.source)}</td>
                <td>${escapeHtml(error.component)}</td>
                <td>${escapeHtml(error.operation)}</td>
                <td>${escapeHtml(error.message)}</td>
                <td><code>${escapeHtml(error.correlationId)}</code></td>
            </tr>
        `).join("");
}

function renderBreakdown(rows, key) {
    if (!rows || rows.length === 0) {
        return `<div class="metric-row"><span class="muted">None</span><strong>-</strong></div>`;
    }

    return rows.slice(0, 6)
        .map(row => metricRow(row[key], formatNumber(row.count)))
        .join("");
}

async function fetchJson(url) {
    const response = await fetch(url, { headers: { "Accept": "application/json" } });
    if (!response.ok) {
        throw new Error(`${url} returned ${response.status}`);
    }

    return response.json();
}

async function postJson(url, payload) {
    const response = await fetch(url, {
        method: "POST",
        headers: {
            "Accept": "application/json",
            "Content-Type": "application/json"
        },
        body: JSON.stringify(payload)
    });

    if (!response.ok) {
        const body = await response.text();
        throw new Error(`${url} returned ${response.status}: ${body.slice(0, 300)}`);
    }

    return response.json();
}

function renderFreshness() {
    const summary = state.freshness?.summary || {};
    els.totalRankedMarkets.textContent = formatNumber(summary.totalRankedMarkets);
    els.visibleMarkets.textContent = formatNumber(summary.visibleMarkets);
    els.hiddenMarkets.textContent = formatNumber(summary.hiddenMarkets);
    els.staleMarkets.textContent = formatNumber(summary.staleMarkets);
    els.eligibleMarkets.textContent = formatNumber(summary.normalRecommendationEligibleMarkets);

    els.platformFreshness.innerHTML = (state.freshness?.platformFreshnessSummary || []).map(platform => `
        <article class="platform-card">
            <h3>
                <span>${escapeHtml(platform.platform)}</span>
                ${platform.isCurrentlyFresh ? chip("Fresh", "ok") : chip("Stale", "bad")}
            </h3>
            ${metricRow("Ranked", formatNumber(platform.totalRankedMarkets))}
            ${metricRow("Visible", formatNumber(platform.visibleMarkets))}
            ${metricRow("Hidden stale", formatNumber(platform.hiddenStaleMarkets))}
            ${metricRow("Latest observed", formatDate(platform.latestObservedAt))}
            ${metricRow("Minutes since latest", formatNumber(platform.minutesSinceLatestObserved))}
            ${metricRow("Over available", formatNumber(platform.overAvailableCount))}
            ${metricRow("Under available", formatNumber(platform.underAvailableCount))}
        </article>
    `).join("");
}

function renderEvSummary() {
    const summary = state.evBacktest?.summary || {};
    const sideBreakdown = state.evBacktest?.sideBreakdown || [];
    const higher = sideBreakdown.find(row => row.side === "Higher") || {};
    const lower = sideBreakdown.find(row => row.side === "Lower") || {};

    els.evSummary.innerHTML = [
        metricRow("Positive EV graded", formatNumber(summary.positiveEvGradedSides)),
        metricRow("Positive EV hit rate", formatPercent(summary.positiveEvHitRate)),
        metricRow("Simulated profit", formatCurrency(summary.simulatedProfitPerOneDollarStake)),
        metricRow("Simulated ROI", formatPercent(summary.simulatedRoi)),
        metricRow("Higher positive-EV ROI", formatPercent(higher.positiveEvSimulatedRoi)),
        metricRow("Lower positive-EV ROI", formatPercent(lower.positiveEvSimulatedRoi)),
        `<div class="metric-row"><span>Research status</span>${chip("Research only", "warn")}</div>`
    ].join("");
}

function renderPropReliability() {
    const summary = state.propReliability?.summary || {};
    const rows = state.propReliability?.propTypes || [];

    els.propReliabilitySummary.innerHTML = [
        metricRow("Prop types", formatNumber(summary.totalPropTypes)),
        metricRow("Profitable candidates", formatNumber(summary.profitableCandidateCount)),
        metricRow("Close but not profitable", formatNumber(summary.closeButNotProfitableCount)),
        metricRow("Excluded for now", formatNumber(summary.excludeForNowCount)),
        metricRow("Insufficient sample", formatNumber(summary.insufficientSampleCount))
    ].join("");

    els.propReliabilityRows.innerHTML = rows.map(row => {
        const statusClass = row.reliabilityClassification === "ProfitableCandidate"
            ? "ok"
            : row.reliabilityClassification === "ExcludeForNow"
                ? "bad"
                : "warn";
        const rowClass = row.propType === "Stolen Bases" ? " class=\"highlight-row\"" : "";

        return `
            <tr${rowClass}>
                <td>${escapeHtml(row.propType)}</td>
                <td>${chip(row.reliabilityClassification, statusClass)}</td>
                <td>${formatNumber(row.positiveEvGradedSides)}</td>
                <td>${formatPercent(row.positiveEvHitRate)}</td>
                <td>${formatPercent(row.positiveEvSimulatedRoi)}</td>
                <td>${formatCurrency(row.positiveEvSimulatedProfit)}</td>
            </tr>
        `;
    }).join("");
}

function populateLineShoppingFilters(groups) {
    setSelectOptions(els.lineShoppingPropTypeFilter, uniqueValues(groups, row => row.normalizedPropType), "All prop types");
    setSelectOptions(els.lineShoppingTrustFilter, uniqueValues(groups, row => row.trustLevel), "All trust levels");
    setSelectOptions(els.lineShoppingStrategyFilter, uniqueValues(groups, row => row.strategyTier), "All strategy tiers");

    if (groups.some(row => row.strategyTier === "ConservativeCandidate")) {
        applyLineShoppingPreset("conservativeCandidates");
    }
}

function applyLineShoppingPreset(preset) {
    const presets = {
        bestResearch: {
            eligibleOnly: true,
            positiveEvOnly: true,
            multiBookOnly: true,
            trust: "",
            strategy: "",
            activeOnly: true,
            includeStale: false,
            includeExpired: false,
            minSample: "50",
            minConfidence: "0.60",
            minRanking: "",
            minBestEv: ""
        },
        conservativeCandidates: {
            eligibleOnly: true,
            positiveEvOnly: false,
            multiBookOnly: false,
            trust: "",
            strategy: "ConservativeCandidate",
            activeOnly: true,
            includeStale: false,
            includeExpired: false,
            minSample: "",
            minConfidence: "",
            minRanking: "",
            minBestEv: ""
        },
        allEligible: {
            eligibleOnly: true,
            positiveEvOnly: false,
            multiBookOnly: false,
            trust: "",
            strategy: "",
            activeOnly: true,
            includeStale: false,
            includeExpired: false,
            minSample: "",
            minConfidence: "",
            minRanking: "",
            minBestEv: ""
        },
        strongOnly: {
            eligibleOnly: true,
            positiveEvOnly: true,
            multiBookOnly: false,
            trust: "Strong",
            strategy: "",
            activeOnly: true,
            includeStale: false,
            includeExpired: false,
            minSample: "50",
            minConfidence: "0.75",
            minRanking: "",
            minBestEv: ""
        },
        multiBookEv: {
            eligibleOnly: true,
            positiveEvOnly: true,
            multiBookOnly: true,
            trust: "",
            strategy: "",
            activeOnly: true,
            includeStale: false,
            includeExpired: false,
            minSample: "",
            minConfidence: "",
            minRanking: "",
            minBestEv: ""
        },
        avoidDiagnostics: {
            eligibleOnly: false,
            positiveEvOnly: false,
            multiBookOnly: false,
            trust: "",
            strategy: "DiagnosticAvoid",
            activeOnly: false,
            includeStale: true,
            includeExpired: true,
            minSample: "",
            minConfidence: "",
            minRanking: "",
            minBestEv: ""
        },
        diagnostics: {
            eligibleOnly: false,
            positiveEvOnly: false,
            multiBookOnly: false,
            trust: "",
            strategy: "",
            activeOnly: false,
            includeStale: true,
            includeExpired: true,
            minSample: "",
            minConfidence: "",
            minRanking: "",
            minBestEv: ""
        },
        reset: {
            eligibleOnly: true,
            positiveEvOnly: true,
            multiBookOnly: true,
            trust: "",
            strategy: "",
            activeOnly: true,
            includeStale: false,
            includeExpired: false,
            minSample: "",
            minConfidence: "",
            minRanking: "",
            minBestEv: ""
        }
    };
    const config = presets[preset] || presets.reset;

    els.lineShoppingEligibleOnlyFilter.checked = config.eligibleOnly;
    els.lineShoppingPositiveEvOnlyFilter.checked = config.positiveEvOnly;
    els.lineShoppingMultiBookOnlyFilter.checked = config.multiBookOnly;
    els.lineShoppingActiveOnlyFilter.checked = config.activeOnly;
    els.lineShoppingIncludeStaleFilter.checked = config.includeStale;
    els.lineShoppingIncludeExpiredFilter.checked = config.includeExpired;
    els.lineShoppingTrustFilter.value = selectHasOption(els.lineShoppingTrustFilter, config.trust) ? config.trust : "";
    els.lineShoppingStrategyFilter.value = selectHasOption(els.lineShoppingStrategyFilter, config.strategy) ? config.strategy : "";
    els.lineShoppingMinSampleFilter.value = config.minSample;
    els.lineShoppingMinConfidenceFilter.value = config.minConfidence;
    els.lineShoppingMinRankingFilter.value = config.minRanking;
    els.lineShoppingMinBestEvFilter.value = config.minBestEv;

    applyLineShoppingFilters();
}

function applyLineShoppingFilters() {
    const groups = state.lineShopping?.topGroupsByBestEstimatedEv || [];
    const propType = els.lineShoppingPropTypeFilter.value;
    const trustLevel = els.lineShoppingTrustFilter.value;
    const strategyTier = els.lineShoppingStrategyFilter.value;
    const minRanking = parseNumber(els.lineShoppingMinRankingFilter.value);
    const minBestEv = parseNumber(els.lineShoppingMinBestEvFilter.value);
    const minSample = parseNumber(els.lineShoppingMinSampleFilter.value);
    const minConfidence = parseNumber(els.lineShoppingMinConfidenceFilter.value);

    state.filteredLineShoppingGroups = groups.filter(group => {
        const bestEv = group.bestEstimatedEv ?? Number.NEGATIVE_INFINITY;
        const isActive = group.lifecycleStatus === "Active";
        const isStale = Boolean(group.isStale);
        const isExpired = Boolean(group.isExpired) || group.lifecycleStatus === "GameStarted" || group.lifecycleStatus === "AwaitingResults";

        return (!propType || group.normalizedPropType === propType)
            && (!trustLevel || group.trustLevel === trustLevel)
            && (!strategyTier || group.strategyTier === strategyTier)
            && (!els.lineShoppingEligibleOnlyFilter.checked || group.isEligibleAsNormalRecommendation)
            && (!els.lineShoppingMultiBookOnlyFilter.checked || (group.bookmakerPrices || []).length > 1)
            && (!els.lineShoppingPositiveEvOnlyFilter.checked || bestEv > 0)
            && (!els.lineShoppingActiveOnlyFilter.checked || isActive)
            && (els.lineShoppingIncludeStaleFilter.checked || !isStale)
            && (els.lineShoppingIncludeExpiredFilter.checked || !isExpired)
            && (minRanking === null || (group.rankingScore ?? 0) >= minRanking)
            && (minBestEv === null || bestEv >= minBestEv)
            && (minSample === null || (group.historicalSampleSize ?? 0) >= minSample)
            && (minConfidence === null || (group.confidenceScore ?? 0) >= minConfidence);
    });

    renderLineShoppingRows();
}

function renderLineShoppingRows() {
    const rows = state.filteredLineShoppingGroups.slice(0, 100);
    els.lineShoppingCount.textContent = `${formatNumber(state.filteredLineShoppingGroups.length)} groups`;
    els.lineShoppingRows.innerHTML = rows.length === 0
        ? `<tr><td colspan="18" class="muted">No line-shopping groups match the current filters.</td></tr>`
        : rows.map(group => `
            <tr class="${lineShoppingLifecycleRowClass(group)}">
                <td>${escapeHtml(group.player)}</td>
                <td>${escapeHtml(group.normalizedPropType)}</td>
                <td>${formatNumber(group.line)}</td>
                <td>${formatEvent(group)}</td>
                <td title="${escapeHtml((group.lifecycleReasons || []).join(" "))}">
                    ${chip(group.lifecycleStatus || "Unknown", lifecycleChip(group.lifecycleStatus))}
                    <div class="muted compact-cell-text">${formatLifecycleTiming(group)}</div>
                </td>
                <td>${chip(group.bestSide || "None", group.bestSide === "None" ? "warn" : "ok")}</td>
                <td>${escapeHtml(group.bestBookmaker)}</td>
                <td>${formatAmericanPrice(group.bestAmericanPrice)}</td>
                <td>${formatNumber(group.bestEstimatedEv)}</td>
                <td>${formatPercent(group.bestProbability)}</td>
                <td title="${escapeHtml(group.sampleWarning || "None")}">${formatNumber(group.historicalSampleSize)}</td>
                <td>${formatPercent(group.confidenceScore)}</td>
                <td>${chip(group.trustLevel || "ResearchOnly", trustChip(group.trustLevel))}</td>
                <td title="${escapeHtml([...(group.strategyReasons || []), ...(group.strategyWarnings || [])].join(" "))}">
                    ${chip(group.strategyClassification || "Research", strategyChip(group.strategyClassification))}
                    <div class="muted compact-cell-text">${escapeHtml(group.strategyTier || "")}</div>
                </td>
                <td title="${escapeHtml((group.riskTags || []).join(", "))}">${escapeHtml(group.marketShape || "")}</td>
                <td>${formatNumber(group.rankingScore)}</td>
                <td>${group.isEligibleAsNormalRecommendation ? chip("Eligible", "ok") : chip("Research", "warn")}</td>
                <td>${renderBookmakerPriceDetails(group.bookmakerPrices || [])}</td>
            </tr>
        `).join("");
}

function renderTopPicks() {
    const groups = state.lineShopping?.comparisonGroups || state.lineShopping?.topGroupsByBestEstimatedEv || [];
    const picks = groups
        .filter(group => group.isCandidate
            && group.lifecycleStatus === "Active"
            && !group.isStale
            && !group.isExpired)
        .sort((left, right) => (right.bestEstimatedEv ?? Number.NEGATIVE_INFINITY) - (left.bestEstimatedEv ?? Number.NEGATIVE_INFINITY))
        .slice(0, 10);

    els.topPicksCount.textContent = `${formatNumber(picks.length)} picks`;
    els.topPicksRows.innerHTML = picks.length === 0
        ? `<div class="empty-state">No active Conservative Candidate picks in the current line-shopping response.</div>`
        : picks.map(group => `
            <article class="top-pick-card">
                <header class="top-pick-header">
                    <div class="top-pick-title">
                        <strong>${escapeHtml(group.player)}</strong>
                        <span>${escapeHtml(group.normalizedPropType)} ${formatNumber(group.line)}</span>
                    </div>
                    <div class="top-pick-badges">
                        ${chip(group.bestSide || "None", group.bestSide === "None" ? "warn" : "ok")}
                        ${chip(group.strategyTier || "Candidate", "ok")}
                        ${chip(group.trustLevel || "Research", trustChip(group.trustLevel))}
                        ${chip(group.marketShape || "Standard", "")}
                    </div>
                </header>
                <div class="top-pick-event">${escapeHtml(formatEventPlain(group))}</div>
                <section class="top-pick-row odds-row">
                    ${topPickStat("Pick", group.bestSide || "-")}
                    ${topPickStat("Book", escapeHtml(group.bestBookmaker || "-"))}
                    ${topPickStat("Odds", `${formatAmericanPrice(group.bestAmericanPrice)} / ${formatDecimalOdds(group.bestDecimalPrice)}`)}
                    ${topPickStat("Risk", escapeHtml(formatRiskToWin(group.bestAmericanPrice)), "wide")}
                </section>
                <section class="top-pick-row">
                    ${topPickStat("Break-even", formatPercent(group.bestImpliedProbability))}
                    ${topPickStat("Model", formatPercent(group.bestProbability), "strong")}
                    ${topPickStat("Edge", formatSignedPercent(group.bestProbabilityEdge), signedValueClass(group.bestProbabilityEdge))}
                </section>
                <section class="top-pick-row">
                    ${topPickStat("EV", formatSignedPercent(group.bestEstimatedEv), signedValueClass(group.bestEstimatedEv))}
                    ${topPickStat("Sample", formatNumber(group.historicalSampleSize))}
                    ${topPickStat("Trust", escapeHtml(group.trustLevel || "-"))}
                    ${topPickStat("Shape", escapeHtml(group.marketShape || "-"))}
                </section>
                ${renderTopPickReasons(group.strategyReasons)}
                ${renderTopPickWarnings([...(group.strategyWarnings || []), ...(group.riskTags || [])])}
            </article>
        `).join("");
}

function topPickStat(label, value, className = "") {
    return `<div class="top-pick-stat ${className}"><span>${label}</span><strong>${value}</strong></div>`;
}

function renderTopPickReasons(values) {
    const items = (values || []).filter(Boolean);
    if (items.length === 0) {
        return "";
    }

    return `<div class="top-pick-reasons"><span>Reasons</span><p>${items.map(escapeHtml).join(" · ")}</p></div>`;
}

function renderTopPickWarnings(values) {
    const items = (values || []).filter(Boolean);
    if (items.length === 0) {
        return "";
    }

    return `<div class="top-pick-warning-tags">${items.map(value => `<span>${escapeHtml(value)}</span>`).join("")}</div>`;
}

function formatDecimalOdds(value) {
    if (value === null || value === undefined || Number.isNaN(Number(value))) {
        return "-";
    }

    return Number(value).toLocaleString(undefined, {
        minimumFractionDigits: 2,
        maximumFractionDigits: 3
    });
}

function formatSignedPercent(value) {
    if (value === null || value === undefined || Number.isNaN(Number(value))) {
        return "-";
    }

    const percent = Number(value) * 100;
    const sign = percent > 0 ? "+" : "";
    return `${sign}${percent.toFixed(2)}%`;
}

function signedValueClass(value) {
    if (value === null || value === undefined || Number.isNaN(Number(value))) {
        return "";
    }

    return Number(value) > 0 ? "positive" : Number(value) < 0 ? "negative" : "";
}

function formatRiskToWin(americanPrice) {
    if (americanPrice === null || americanPrice === undefined) {
        return "-";
    }

    const price = Number(americanPrice);
    if (!Number.isFinite(price) || price === 0) {
        return "-";
    }

    return price < 0
        ? `Risk $${formatNumber(Math.abs(price))} to win $100`
        : `Risk $100 to win $${formatNumber(price)}`;
}

function lineShoppingLifecycleRowClass(group) {
    if (group.isExpired || group.lifecycleStatus === "AwaitingResults" || group.lifecycleStatus === "GameStarted") {
        return "expired-row";
    }

    return group.isStale || group.lifecycleStatus === "Stale" ? "stale-row" : "";
}

function formatLifecycleTiming(group) {
    if (group.minutesUntilStart !== null && group.minutesUntilStart !== undefined) {
        return group.minutesUntilStart >= 0
            ? `${formatNumber(group.minutesUntilStart)} min to start`
            : `${formatNumber(Math.abs(group.minutesUntilStart))} min since start`;
    }

    if (group.minutesSinceObserved !== null && group.minutesSinceObserved !== undefined) {
        return `${formatNumber(group.minutesSinceObserved)} min old`;
    }

    return "-";
}

function renderBookmakerPriceDetails(prices) {
    if (prices.length === 0) {
        return "<span class=\"muted\">-</span>";
    }

    return `
        <details>
            <summary>${formatNumber(prices.length)} books</summary>
            <div class="book-price-list">
                ${prices.map(price => `
                    <div class="book-price-row">
                        <strong>${escapeHtml(price.bookmaker)}</strong>
                        <span>O ${formatAmericanPrice(price.overAmericanPrice)} / U ${formatAmericanPrice(price.underAmericanPrice)}</span>
                        <span>EV O ${formatNumber(price.overEstimatedEv)} / U ${formatNumber(price.underEstimatedEv)}</span>
                        <span>${formatDate(price.observedAt)}</span>
                    </div>
                `).join("")}
            </div>
        </details>
    `;
}

function populateFilters(markets) {
    setSelectOptions(els.platformFilter, uniqueValues(markets, row => row.platform || row.sportsbook), "All platforms");
    setSelectOptions(els.classificationFilter, uniqueValues(markets, row => row.marketClassification), "All classes");
    setSelectOptions(els.propTypeFilter, uniqueValues(markets, row => row.normalizedPropType), "All prop types");
    setSelectOptions(els.recommendationFilter, uniqueValues(markets, row => row.recommendation), "All recommendations");
}

function applyFilters() {
    const markets = state.rankings?.results || [];
    const platform = els.platformFilter.value;
    const classification = els.classificationFilter.value;
    const propType = els.propTypeFilter.value;
    const recommendation = els.recommendationFilter.value;
    const minProbability = parseNumber(els.minProbabilityFilter.value);
    const minRanking = parseNumber(els.minRankingFilter.value);

    state.filteredMarkets = markets.filter(row => {
        const rowPlatform = row.platform || row.sportsbook || "";
        const maxProbability = Math.max(row.probabilityOver ?? 0, row.probabilityUnder ?? 0);

        return (!platform || rowPlatform === platform)
            && (!classification || row.marketClassification === classification)
            && (!propType || row.normalizedPropType === propType)
            && (!recommendation || row.recommendation === recommendation)
            && (!els.visibleOnlyFilter.checked || row.isVisibleInResearchDashboard)
            && (!els.eligibleOnlyFilter.checked || row.isEligibleAsNormalRecommendation)
            && (minProbability === null || maxProbability >= minProbability)
            && (minRanking === null || (row.rankingScore ?? 0) >= minRanking);
    });

    renderMarketRows();
}

function renderMarketRows() {
    const rows = state.filteredMarkets.slice(0, 300);
    els.tableCount.textContent = `${formatNumber(state.filteredMarkets.length)} markets`;
    els.marketRows.innerHTML = rows.map(row => `
        <tr>
            <td>${escapeHtml(row.player)}</td>
            <td>${escapeHtml(row.platform || row.sportsbook)}</td>
            <td>${escapeHtml(row.rawSportsbookLabel)}</td>
            <td>${escapeHtml(row.normalizedPropType)}</td>
            <td>${formatNumber(row.sportsbookLine)}</td>
            <td>${chip(row.marketClassification, classificationChip(row.marketClassification))}</td>
            <td>${escapeHtml(row.payoutType)}</td>
            <td>${formatPercent(row.probabilityOver)}</td>
            <td>${formatPercent(row.probabilityUnder)}</td>
            <td>${formatNumber(row.rankingScore)}</td>
            <td>${row.isVisibleInResearchDashboard ? chip("Visible", "ok") : chip("Hidden", "bad")}</td>
            <td>${row.isEligibleAsNormalRecommendation ? chip("Eligible", "ok") : chip("Ineligible", "warn")}</td>
            <td>${formatList(row.displayWarnings)}</td>
            <td>${formatList(row.ineligibilityReasons)}</td>
        </tr>
    `).join("");
}

async function copyRecentErrors() {
    const payload = JSON.stringify(state.recentErrors || { errors: [] }, null, 2);

    try {
        await navigator.clipboard.writeText(payload);
        setStatus("Recent error JSON copied.");
    } catch {
        setStatus("Copy failed. Select recent error JSON from the network response instead.", true);
    }
}

function setSelectOptions(select, values, allLabel) {
    const current = select.value;
    select.innerHTML = `<option value="">${allLabel}</option>` + values
        .map(value => `<option value="${escapeHtml(value)}">${escapeHtml(value)}</option>`)
        .join("");
    select.value = values.includes(current) ? current : "";
}

function selectHasOption(select, value) {
    return !value || Array.from(select.options).some(option => option.value === value);
}

function uniqueValues(rows, selector) {
    return [...new Set(rows.map(selector).filter(Boolean))].sort((a, b) => a.localeCompare(b));
}

function metricRow(label, value) {
    return `<div class="metric-row"><span>${escapeHtml(label)}</span><strong>${value ?? "-"}</strong></div>`;
}

function chip(label, tone = "") {
    return `<span class="chip ${tone}">${escapeHtml(label ?? "-")}</span>`;
}

function classificationChip(classification) {
    if (classification === "Standard") return "ok";
    if (classification === "Demon" || classification === "Goblin") return "bad";
    return "warn";
}

function trustChip(trustLevel) {
    if (trustLevel === "Strong") return "ok";
    if (trustLevel === "Medium") return "ok";
    if (trustLevel === "Weak") return "warn";
    return "warn";
}

function strategyChip(classification) {
    if (classification === "Candidate") return "ok";
    if (classification === "Watchlist") return "warn";
    if (classification === "Avoid") return "bad";
    if (classification === "ResearchOnly") return "warn";
    return "";
}

function lifecycleChip(status) {
    if (status === "Active") return "ok";
    if (status === "Stale") return "warn";
    if (status === "GameStarted" || status === "AwaitingResults" || status === "Expired") return "bad";
    if (status === "Graded") return "ok";
    return "warn";
}

function workflowStatusChip(status) {
    if (status === "Completed" || status === "BacktestReady" || status === "LineShoppingReady") return "ok";
    if (status === "NeedsAttention") return "bad";
    return "warn";
}

function statusChip(status) {
    if (status === "Healthy" || status === "Fresh" || status === "None" || status === "Info") return "ok";
    if (status === "Error" || status === "Blocked" || status === "Stale") return "bad";
    return "warn";
}

function formatList(values) {
    if (!values || values.length === 0) {
        return "<span class=\"muted\">-</span>";
    }

    return values.map(value => `<span class="chip warn">${escapeHtml(value)}</span>`).join(" ");
}

function formatNumber(value) {
    if (value === null || value === undefined || Number.isNaN(Number(value))) {
        return "-";
    }

    return Number(value).toLocaleString(undefined, { maximumFractionDigits: 4 });
}

function formatPercent(value) {
    if (value === null || value === undefined || Number.isNaN(Number(value))) {
        return "-";
    }

    return `${(Number(value) * 100).toFixed(2)}%`;
}

function formatCurrency(value) {
    if (value === null || value === undefined || Number.isNaN(Number(value))) {
        return "-";
    }

    return Number(value).toLocaleString(undefined, {
        style: "currency",
        currency: "USD",
        maximumFractionDigits: 2
    });
}

function formatAmericanPrice(value) {
    if (value === null || value === undefined || Number.isNaN(Number(value))) {
        return "-";
    }

    const number = Number(value);
    return number > 0 ? `+${number}` : `${number}`;
}

function formatEvent(group) {
    const teams = [group.awayTeam, group.homeTeam].filter(Boolean).join(" @ ");
    const start = formatDate(group.eventStartTime);

    return `${escapeHtml(teams || group.eventId || "-")}<br><span class="muted">${escapeHtml(start)}</span>`;
}

function formatEventPlain(group) {
    const teams = [group.awayTeam, group.homeTeam].filter(Boolean).join(" @ ");
    const start = formatDate(group.eventStartTime);

    return `${teams || group.eventId || "-"} (${start})`;
}

function formatDate(value) {
    if (!value) {
        return "-";
    }

    return new Date(value).toLocaleString();
}

function parseNumber(value) {
    if (value === "") {
        return null;
    }

    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
}

function escapeHtml(value) {
    return String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll("\"", "&quot;")
        .replaceAll("'", "&#039;");
}

function setStatus(message, severity = "info") {
    const normalizedSeverity = severity === true
        ? "error"
        : severity === false
            ? "info"
            : severity || "info";
    const autoDismissMs = normalizedSeverity === "error"
        ? null
        : normalizedSeverity === "warning" || normalizedSeverity === "warn"
            ? 8000
            : 4000;

    if (state.statusTimerId) {
        window.clearTimeout(state.statusTimerId);
        state.statusTimerId = null;
    }

    els.statusMessage.textContent = message;
    els.status.className = `status ${normalizedSeverity}`;
    els.status.hidden = false;

    if (autoDismissMs !== null) {
        state.statusTimerId = window.setTimeout(dismissStatus, autoDismissMs);
    }
}

function dismissStatus() {
    if (state.statusTimerId) {
        window.clearTimeout(state.statusTimerId);
        state.statusTimerId = null;
    }

    els.status.hidden = true;
}

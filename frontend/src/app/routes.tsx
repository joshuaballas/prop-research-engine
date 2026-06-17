import { Navigate, Route, Routes } from 'react-router-dom';
import { AppLayout } from '../layouts/AppLayout';
import { AuthLayout } from '../layouts/AuthLayout';
import { PublicLayout } from '../layouts/PublicLayout';
import { AccountPage } from '../pages/app/AccountPage';
import { BillingPage } from '../pages/app/BillingPage';
import { DashboardPage } from '../pages/app/DashboardPage';
import { EntryBuilderPage } from '../pages/app/EntryBuilderPage';
import { LineShoppingPage } from '../pages/app/LineShoppingPage';
import { MarketsPage } from '../pages/app/MarketsPage';
import { PerformancePage } from '../pages/app/PerformancePage';
import { TopPicksPage } from '../pages/app/TopPicksPage';
import { EntryBuilderProvider } from '../hooks/useEntryBuilder';
import { CreateAccountPage } from '../pages/public/CreateAccountPage';
import { HomePage } from '../pages/public/HomePage';
import { PricingPage } from '../pages/public/PricingPage';
import { SignInPage } from '../pages/public/SignInPage';

export function AppRoutes() {
  return (
    <Routes>
      <Route element={<PublicLayout />}>
        <Route path="/" element={<HomePage />} />
        <Route path="/pricing" element={<PricingPage />} />
      </Route>
      <Route element={<AuthLayout />}>
        <Route path="/sign-in" element={<SignInPage />} />
        <Route path="/create-account" element={<CreateAccountPage />} />
      </Route>
      <Route
        path="/app"
        element={
          <EntryBuilderProvider>
            <AppLayout />
          </EntryBuilderProvider>
        }
      >
        <Route index element={<Navigate to="/app/dashboard" replace />} />
        <Route path="dashboard" element={<DashboardPage />} />
        <Route path="markets" element={<MarketsPage />} />
        <Route path="top-picks" element={<TopPicksPage />} />
        <Route path="line-shopping" element={<LineShoppingPage />} />
        <Route path="entry-builder" element={<EntryBuilderPage />} />
        <Route path="performance" element={<PerformancePage />} />
        <Route path="account" element={<AccountPage />} />
        <Route path="billing" element={<BillingPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

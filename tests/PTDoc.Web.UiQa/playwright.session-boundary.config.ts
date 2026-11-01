import { defineConfig } from '@playwright/test';
import shared from './playwright.config';

// Explicitly selected by the disposable localhost harness, never ordinary QA.
export default defineConfig(shared, {
  testIgnore: [],
  testMatch: '**/session-circuit-boundary.spec.ts'
});

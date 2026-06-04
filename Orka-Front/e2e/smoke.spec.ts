import { test, expect } from "@playwright/test";

test.describe("Frontend App Shell and Routes Smoke Tests", () => {
  test("should load the landing page successfully", async ({ page }) => {
    await page.goto("/");
    await expect(page).toHaveTitle(/Orka AI/);
    
    // Check that important elements of the landing page are visible
    const logo = page.locator("svg").first(); // Logo or first svg
    await expect(logo).toBeVisible();
    
    // Landing page has product copy
    await expect(page.locator("body")).toContainText("Orka");
  });

  test("should load the login page successfully", async ({ page }) => {
    await page.goto("/login");
    
    // The login page has email and password inputs
    const emailInput = page.locator('input[type="email"]');
    const passwordInput = page.locator('input[type="password"]');
    
    await expect(emailInput).toBeVisible();
    await expect(passwordInput).toBeVisible();
  });

  test("should redirect protected routes to login", async ({ page }) => {
    // Navigating to a protected route should redirect to login page
    await page.goto("/app");
    await expect(page).toHaveURL(/\/login/);
    
    await page.goto("/profile");
    await expect(page).toHaveURL(/\/login/);

    await page.goto("/courses");
    await expect(page).toHaveURL(/\/login/);
  });
});

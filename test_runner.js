const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const SCREENSHOT_DIR = "d:\\Orka\\screenshots";
if (!fs.existsSync(SCREENSHOT_DIR)) {
    fs.mkdirSync(SCREENSHOT_DIR);
}

async function runTest() {
    console.log("Starting Orka E2E Python Curriculum Test...");
    const browser = await chromium.launch({ headless: false, slowMo: 50 });
    const context = await browser.newContext();
    context.setDefaultTimeout(120000);
    const page = await context.newPage();
    page.setDefaultTimeout(120000);

    let step = 1;
    const takeScreenshot = async (name) => {
        await page.screenshot({ path: path.join(SCREENSHOT_DIR, `${String(step).padStart(2, '0')}-${name}.png`) });
        console.log(`Screenshot saved: ${name}`);
        step++;
    };

    try {
        console.log("Registering via API to bypass UI flakiness...");
        const uniqueEmail = `pythonqa_${Date.now()}@test.com`;
        
        const response = await fetch("http://localhost:5065/api/auth/register", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ email: uniqueEmail, password: "Test12345", name: "Python QA" })
        });
        
        if (!response.ok) {
            console.error("API Registration failed!", await response.text());
            throw new Error("API Reg failed");
        }
        const data = await response.json();
        const token = data.token;
        
        console.log("Navigating to App directly with auth token...");
        // Define localStorage via init script
        await context.addInitScript((token) => {
            window.localStorage.setItem('orka_token', token);
        }, token);

        await page.goto("http://localhost:3000/app");
        await page.waitForTimeout(2000);
        await takeScreenshot("chat-loaded");

        // ----------------------------------------------------
        // PHASE 1: Baseline Quiz request
        console.log("Sending /plan Python öğrenmek istiyorum...");
        await page.fill('textarea', '/plan Python öğrenmek istiyorum');
        await page.keyboard.press('Enter');
        await takeScreenshot("plan-sent");

        // Wait for the Baseline Quiz message
        console.log("Waiting for Baseline Quiz response...");
        // Usually contains "Harika!" or "ölçmeliyim"
        await page.waitForFunction(() => {
            const texts = Array.from(document.querySelectorAll('.prose')).map(p => p.textContent || "");
            return texts.some(t => t.includes("seviyeni ölçmeliyim") || t.includes("akademik planlama süreci başlatıyorum"));
        }, { timeout: 30000 });
        await takeScreenshot("baseline-quiz-received");

        // ----------------------------------------------------
        // PHASE 2: Answer the Quiz
        console.log("Answering the Baseline Quiz...");
        await page.fill('textarea', 'Gerçekten hiç bilmiyorum, bana en temelden ve basit bir dille öğrenme planı çıkar.');
        await page.keyboard.press('Enter');
        await takeScreenshot("quiz-answer-sent");

        // Wait for the Deep Plan response and first lesson
        console.log("Waiting for DeepPlan to generate the curriculum and first lesson... (This may take up to 20-30s)");
        await page.waitForFunction(() => {
            const texts = Array.from(document.querySelectorAll('.prose')).map(p => p.textContent || "");
            return texts.some(t => t.includes("Senin için aşağıdaki öğrenme haritasını") || t.includes("İlk Konumuz"));
        }, { timeout: 90000 });
        
        // Wait another bit to ensure stream finishes
        await page.waitForTimeout(5000); 
        await takeScreenshot("lesson-started-and-plan-generated");

        // ----------------------------------------------------
        // PHASE 3: Finish First Lesson and check Wiki
        console.log("Finishing the first lesson...");
        await page.fill('textarea', 'Süper, çok iyi anladım. Tamamen bitirdim bu konuyu.');
        await page.keyboard.press('Enter');
        await takeScreenshot("lesson-finish-sent");

        // Wait for analyzer and summarizer
        console.log("Waiting for the lesson to finish and Wiki to compile...");
        await page.waitForTimeout(15000); // Wait for background Tasks
        await takeScreenshot("lesson-completed-response");

        console.log("Opening Wiki Drawer...");
        // The wiki button is usually a Book type icon in the header
        await page.click('button:has(svg.lucide-book)');
        await page.waitForTimeout(2000);
        await takeScreenshot("wiki-drawer-opened");

        // Check if Wiki has actual content (h1 or h2 tags usually)
        const wikiContent = await page.$$eval('.prose-invert', els => els.map(e => e.textContent));
        if (wikiContent.length === 0 || !wikiContent[0] || wikiContent[0].trim() === "") {
            console.error("WIKI IS EMPTY OR NOT FOUND! FAILED!");
        } else {
            console.log(`Wiki content extracted snippet: ${wikiContent[0]?.substring(0, 150) || "EMPTY"}`);
            const isMissing = wikiContent[0].includes("henüz oluşturulmadı") || wikiContent[0].includes("Özet bulunamadı");
            if (isMissing) {
                console.error("WIKI HAS NO REAL CONTENT! IT SHOWS THE PLACEHOLDER MESSAGE!");
            } else {
                console.log("TEST SUCCESSFUL! 🚀");
            }
        }

    } catch (e) {
        console.error("Test failed:", e);
        const lastTexts = await page.$$eval('.prose', els => els.map(e => e.textContent));
        console.log("--- LAST TEXTS ON SCREEN ---");
        console.log(lastTexts[lastTexts.length - 1]);
        await takeScreenshot("error-state");
    } finally {
        await browser.close();
    }
}

runTest();

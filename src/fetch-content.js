const { chromium } = require('playwright');

async function fetchContent(url) {
    const browser = await chromium.launch({ headless: true });
    const page = await browser.newPage();

    try {
        await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 30000 });

        const title = await page.title();

        // Remove code blocks, images, figures, and other non-text elements before extracting text
        await page.evaluate(() => {
            const article = document.querySelector('article[data-clarity-region="article"] div.entry-content');
            if (article) {
                article.querySelectorAll('pre, code, img, figure, svg, script, style, video, iframe, table').forEach(el => el.remove());
            }
        });

        // Extract text content from the article body
        const content = await page.evaluate(() => {
            const article = document.querySelector('article[data-clarity-region="article"] div.entry-content');
            if (!article) {
                // Fallback: try broader selectors
                const fallback = document.querySelector('article .entry-content') ||
                                 document.querySelector('.entry-content') ||
                                 document.querySelector('article');
                if (fallback) {
                    fallback.querySelectorAll('pre, code, img, figure, svg, script, style, video, iframe, table').forEach(el => el.remove());
                    return fallback.innerText || '';
                }
                return '';
            }
            return article.innerText || '';
        });

        // Trim whitespace and collapse multiple newlines
        const cleanContent = content.replace(/\n{3,}/g, '\n\n').trim();

        const result = { title: title, content: cleanContent, error: '' };
        process.stdout.write(JSON.stringify(result));
    } catch (error) {
        const result = { title: '', content: '', error: error.message };
        process.stdout.write(JSON.stringify(result));
    } finally {
        await browser.close();
    }
}

const url = process.argv[2];
if (!url) {
    process.stderr.write('Usage: node fetch-content.js <url>\n');
    process.exit(1);
}

fetchContent(url);

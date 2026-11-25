'use strict';
const { spawnSync } = require('node:child_process');

// Share the Windows command name and bound cleanup so a failed browser stop cannot hang CI.
module.exports = browser => {
    if (!browser?.pid || browser.exitCode !== null) return;
    const hasExited = () => {
        if (browser.exitCode !== null) return true;
        try { process.kill(browser.pid, 0); return false; }
        catch (error) { return error.code === 'ESRCH'; }
    };
    try {
        if (process.platform === 'win32') {
            const result = spawnSync('taskkill', ['/pid', String(browser.pid), '/T', '/F'], { encoding: 'utf8', timeout: 10000 });
            // The browser can exit after the initial check but before taskkill runs.
            // Accept that only after checking the process; genuine cleanup failures still fail CI.
            if ((result.error || result.status !== 0) && !hasExited()) {
                console.error('Unable to stop the test browser:', result.error?.code || `taskkill exit ${result.status}`,
                    String(result.stderr || '').trim().slice(0, 1000));
                process.exitCode = 1;
            }
        } else if (!browser.kill('SIGTERM') && !hasExited()) {
            console.error('Unable to stop the test browser: SIGTERM was not sent.');
            process.exitCode = 1;
        }
    } catch (error) {
        if (!hasExited()) {
            console.error('Unable to stop the test browser:', error.code || error.message);
            process.exitCode = 1;
        }
    } finally {
        browser.unref();
    }
};

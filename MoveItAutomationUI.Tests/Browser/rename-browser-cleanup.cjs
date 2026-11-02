'use strict';
const { spawnSync } = require('node:child_process');

// Share the Windows command name and bound cleanup so a failed browser stop cannot hang CI.
module.exports = browser => {
    if (!browser?.pid || browser.exitCode !== null) return;
    try {
        if (process.platform === 'win32') {
            const result = spawnSync('taskkill', ['/pid', String(browser.pid), '/T', '/F'], { stdio: 'ignore', timeout: 10000 });
            if (result.error || result.status !== 0) {
                console.error('Unable to stop the rename test browser.');
                process.exitCode = 1;
            }
        } else if (!browser.kill('SIGTERM')) {
            console.error('Unable to stop the rename test browser.');
            process.exitCode = 1;
        }
    } catch {
        console.error('Unable to stop the rename test browser.');
        process.exitCode = 1;
    } finally {
        browser.unref();
    }
};

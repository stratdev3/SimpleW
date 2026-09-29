// Embedded in the NuGet assembly. Node runs this source with --eval, without a shell.
const { spawn } = require('node:child_process');
const [npmCli, ...args] = process.argv.slice(1);
let child;
let stopping = false;
let started = false;
let input = '';

/**
 * Stops the supervisor once, terminating its Unix process group before exiting.
 * On Windows, the C# Job Object handles descendant cleanup.
 * @param {number} code Exit code reported by the supervisor.
 */
function stop(code = 0) {
    if (stopping) return;
    stopping = true;
    if (child?.pid && process.platform !== 'win32') {
        // npm and all scripts it starts share this private process group on Unix.
        try { process.kill(-child.pid, 'SIGTERM'); } catch {}
        setTimeout(() => {
            try { process.kill(-child.pid, 'SIGKILL'); } catch {}
            process.exit(code);
        }, 500);
    } else {
        // On Windows the C# Job Object owns this process and every descendant.
        process.exit(code);
    }
}

process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => {
    input += chunk;
    let end;
    while ((end = input.indexOf('\n')) >= 0) {
        const command = input.slice(0, end).trim();
        input = input.slice(end + 1);
        if (command === 'stop') stop();
        if (command === 'start' && !started && !stopping) {
            started = true;
            child = spawn(process.execPath, [npmCli, ...args], {
                stdio: ['ignore', 'inherit', 'inherit'],
                windowsHide: true,
                detached: process.platform !== 'win32'
            });
            child.on('error', error => { console.error(error.message); stop(1); });
            child.on('exit', code => stop(code ?? 1));
        }
    }
});
// The pipe closes even when the IDE kills the C# process without running finally blocks.
process.stdin.on('end', () => stop());
process.stdin.on('error', () => stop(1));
process.on('SIGTERM', () => stop());
process.on('SIGINT', () => stop());

# WSL ext4 / loop-backed Btrfs benchmark

Run `tools/wsl-btrfs-bench.sh` with Bash **inside the WSL distribution being
measured**. This is independent of iKeyd's application and WSL provisioning code.
The script installs missing fio, btrfs-progs, Git and Python packages through apt.
It needs mount/loop privileges, Btrfs kernel support, and ext4 at `/var/tmp`.
Use a quiet, dedicated WSL distribution: `sync` and cache dropping affect other
workloads sharing the kernel. Avoid other disk-intensive work during measurement.

## Start here

Inside WSL, from a checkout of `bench/wsl-loop-btrfs`:

```bash
git switch bench/wsl-loop-btrfs
git pull --ff-only
sudo -v
bash tools/wsl-btrfs-bench.sh --smoke
```

The entry point must have LF line endings; `.gitattributes` enforces this even for
Windows checkouts. Do not launch it with PowerShell or `sh`. A password prompt is
announced before preparation. Noninteractive execution without cached sudo
credentials fails with instructions, rather than waiting invisibly. Running the
whole script as root is also supported, but cleanup must use that same user.

`--smoke` runs the same seven conditions, provisioning, fio, Git, small-file,
summary and cleanup paths with 1 GiB images, 16 MiB fio files, 1-second fio jobs,
100 Git files, and 1,000 small files x 3. Smoke results are **not** full measurements.
No test-only provisioning or injected dependencies are used.

For full measurement with the default 16 GiB images, including 300,000 small files x 3 for
**each** condition:

```bash
sudo -v
IMAGE_GIB=16 bash tools/wsl-btrfs-bench.sh
```

Defaults are 16 GiB images, 1 GiB fio files, 15 seconds per fio job, 15,000 Git
files x 3, and 300,000 small files x 3. Full mode rejects reduced small-file counts.
Runtime depends on the machine; there is no 30-minute cutoff or automatic skipping.
`RUNTIME`, `IMAGE_GIB`, `FIO_SIZE` (integer K/M/G), `GIT_FILES`, `GIT_RUNS`,
`COMPRESS_PCT` (default 60), `DROP_CACHES` and `KEEP_MOUNTS` are recorded settings.
Each Btrfs condition gets a fresh image in every round. Normal mode cleans it up
after that measurement; KEEP_MOUNTS=1 retains the six images from the final round.
The free-space check budgets one full image in normal mode (about 26 GiB total
with defaults) or six in keep mode (about 106 GiB), plus baseline data and headroom.
It checks available ext4 bytes/inodes and includes a conservative 24 KiB per
small file for Btrfs metadata DUP, CoW and block-group reservations. A 4 GiB image
ran out of space during real 300,000-file validation and is now rejected before
provisioning. The 16 GiB default is recommended for full measurement.

**Also check the Windows drive containing this distribution's VHDX.** WSL's `df`
can report far more free space than the host drive actually has. In PowerShell:

```powershell
Get-Volume -DriveLetter C | Select-Object SizeRemaining, Size
```

Use the actual VHDX drive letter. Pass a conservative whole-GiB budget from that
reading to make it an enforced additional limit, for example **only if at least
40 GiB is currently available**:

```bash
HOST_FREE_GIB=40 IMAGE_GIB=16 bash tools/wsl-btrfs-bench.sh
```

The script explicitly reports when host space was not checked. It never deletes,
formats, resizes or compacts a WSL distribution or VHDX. Deleting benchmark images
frees ext4 space but does not promise to shrink the Windows VHDX allocation.

## Conditions and measurement meaning

The matrix is ext4 directly, sparse-none/LZO/ZSTD, and
fixed-preallocated-none/LZO/ZSTD. ZSTD remains `zstd:3`. Sparse images use
`truncate`; preallocated images use `fallocate`. All mkfs calls use `-K` to prevent
discard from punching holes in preallocated images. All Btrfs mounts explicitly
request `nodiscard`. Logical size and actual allocated bytes are recorded before
mkfs, after each round's setup and workload; losing preallocation fails the run.
Only newly created, exclusively owned image/loop identities can reach mkfs.

Before every workload the script checks the Btrfs mountpoint, loop backing image,
filesystem UUID and compression options. A missing mount cannot silently turn into
an ext4 measurement. Mount options and successful identity checks are saved.

Fio measures sequential read/write, random read/write and fsync-after-every-write.
It uses a fixed seed, identical size and compression settings across conditions,
and completely initializes the fio file before timed reads. `direct=1` records the
fio O_DIRECT request; it is not proof of the kernel's complete internal I/O path.
Buffered jobs use `direct=0`. Cold means `sync` and a successful cache drop before
the job, not that every I/O during a time-based job remains cold. Warm sequential
write explicitly reads the entire file first and does **not** drop caches next.
The kernel still controls residency; Windows storage caches are not flushed.

By default cache-drop failure aborts. Explicit `DROP_CACHES=0` runs are labeled
`uncontrolled`, never cold. See `cache.tsv` and `environment.txt` before comparing
runs. Small-file create/stat/read/rename/delete phases run consecutively, so later
phases reuse cache populated by earlier phases. Sync phases are separately timed.
All conditions use identical 1,024-byte small-file content and directory layout.
Automatic Git maintenance is disabled only in the synthetic source repository;
preparation finishes its explicit GC before measurement, avoiding background GC
races. Git clones the same synthetic source with `--no-local`, then measures Git status;
its source is on the same outer ext4 filesystem for all conditions.

Small-file and Git measurements run in rounds. Ext4 runs first, fourth and last;
Btrfs order is reversed/rotated between rounds. `order.tsv` records the actual
sequence. Every round recreates the Btrfs filesystem under both cleanup and keep modes;
only the final round is retained by keep mode. Fio data is removed and synced
before the small-file workload to avoid carrying an unrelated large file into it.
Fio runs once per condition in the first round; its one-sample results
cannot quantify run-to-run variance. This ordering reduces, but does not eliminate,
thermal and background-load bias. Comparisons should use matching settings.

## Progress, results and failure

Startup prints the work, mount, result and log directories. Every stage prints
start/end timestamps and elapsed seconds; a 15-second heartbeat names the current
stage during long commands, including preparation. Small-file phase completion is
printed without adding per-file progress I/O to the timing loop.

Each invocation creates a unique `~/wsl-btrfs-bench-results-<time>-<suffix>/`:

- `run.log`, `current-stage`, `exit-code`: diagnostics, including failed commands.
- `environment.txt`, `mounts.tsv`, `mount-checks.tsv`, `cache.tsv`, `order.tsv`:
  configuration and execution evidence (mounts.tsv includes historical rounds).
- `fio_*.json`, `smallfiles_*.json`, `git_*.json`: individual raw measurements;
  small-file JSON includes the count and separate operation timings.
- `storage_*.tsv`, `usage_*.txt`: logical/physical allocation and Btrfs usage.
- `summary.tsv`, `summary.txt`: median, min/max, population standard deviation and
  all run times. Missing, zero, nonfinite or fio-error results fail aggregation.

A nonzero exit is a failed run, even if some raw data exists. Failure logs and
partial results survive cleanup. SIGINT/Ctrl+C exits 130; SIGTERM exits 143.
A command in uninterruptible kernel I/O may delay handling a signal; the heartbeat
still identifies the stage. No success is reported before measurement validation
and, in automatic mode, successful cleanup.

## Keep mounts and clean up

```bash
KEEP_MOUNTS=1 IMAGE_GIB=16 bash tools/wsl-btrfs-bench.sh
# Or check preservation with a short run:
KEEP_MOUNTS=1 bash tools/wsl-btrfs-bench.sh --smoke
# Later, as the SAME Linux user, in the SAME distribution:
bash tools/wsl-btrfs-bench.sh --cleanup
```

`KEEP_MOUNTS=1` retains mounts, loops, images and investigation directories on
success. On error or Ctrl+C it retains whatever was created, including partial
files. The final output and `mounts.tsv` map mountpoints to images and loops;
`/var/tmp/wsl-btrfs-bench-$UID` contains the recovery/ownership records. A new run
refuses existing retained data and never removes it from its startup error trap.
Normal mode attempts cleanup on success, failure and interrupt.

Cleanup verifies ownership and backing identities before unmount/detach. A busy
mount, unexpected mount/device, pending detach or unjournaled image stops cleanup
with nonzero status and preserves the affected resource. Release investigation
shells/files or extra bind mounts, inspect the log, and rerun `--cleanup`. It never
recursively removes a failed unmount target. Repeat cleanup with no resources is
safe. A lock excludes concurrent run/cleanup by the same user.

Keep the WSL terminal/session open while investigating. Mounts are not persistent across WSL shutdown or loss of its mount namespace; images and result files remain on disk. After a restart, use cleanup to remove the recorded resources before a new run.

Ownership-free resources left by the old script are deliberately refused rather
than guessed at. Inspect old image/mount/loop identities manually; do not use a
blanket recursive deletion. After a power loss or SIGKILL, resources may need the
same manual inspection, especially if loop attachment happened before its journal
could be written. Other users' mounts and loops are outside cleanup's ownership.

## Execution evidence (2026-09-15)

Validation ran as root in a newly installed Ubuntu 24.04 WSL2 distribution,
`iKeyd-bench`, with kernel `6.18.33.2-microsoft-standard-WSL2`, fio 3.36 and
btrfs-progs 6.6.3. No automated test files were added.

The original script, with LF endings and a Linux-only PATH, exited **1** at
`capture_env` immediately after `command -v wsl.exe`; the trace never reached Git
preparation or measurement. With wsl.exe available the original did start work.
A Windows CRLF checkout separately failed on `set -Eeuo pipefail` at line 2.
Full-size validation also exposed a race between Git's automatic GC and explicit
GC, and a real ENOSPC with 4 GiB images; both are addressed in the current path.

Commands executed from the WSL checkout included:

```bash
bash -n tools/wsl-btrfs-bench.sh
shellcheck tools/wsl-btrfs-bench.sh
PATH=/usr/sbin:/usr/bin:/sbin:/bin HOST_FREE_GIB=35 KEEP_MOUNTS=1 bash tools/wsl-btrfs-bench.sh --smoke
bash tools/wsl-btrfs-bench.sh --cleanup
PATH=/usr/sbin:/usr/bin:/sbin:/bin HOST_FREE_GIB=35 bash tools/wsl-btrfs-bench.sh
```

The full run used the default 16 GiB images, 300,000 files x 3 x 7 conditions,
15,000 Git files x 3 and 15-second/1-GiB fio jobs. It completed in **1,323 seconds**,
including aggregation and automatic cleanup, with exit **0**. The output contains
21 small-file JSONs (all recording 300,000 files), 21 Git JSONs and 49 fio JSONs
(42 measured jobs plus seven initialization jobs). Work and mount directories
were absent after cleanup. Run ID: `20260915-044740-v7GrIR`.

The seven-condition keep-mode smoke completed in 117 seconds with exit 0 and six
actual Btrfs mounts left for inspection. Re-running against retained data failed
without deleting it. A process holding a mount busy made cleanup fail with exit 1
and preserved its mount, loop and image; releasing it allowed cleanup to succeed.
Actual Ctrl+C during fio returned 130 with logs retained, preserving the active
mount in keep mode and removing it in normal mode. SIGTERM returned 143 and
cleaned up. A DROP_CACHES=0 keep-mode smoke also completed all seven conditions
with exit 0: seven uncontrolled sequential-write results and no cold-labeled jobs. A deliberately removed
Btrfs mount stopped measurement before any ext4 fallback; undersized images and
an insufficient host budget were rejected before provisioning.

Full-sized simultaneous keep mode was not run because host space was insufficient;
retention was verified with the same path at smoke size. These are WSL root-user
checks, not acceptance claims for every kernel, distribution or sudo policy.

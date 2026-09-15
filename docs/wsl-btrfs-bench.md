# WSL direct ext4 and loop/VHDX ext4/Btrfs benchmark

Run `tools/wsl-btrfs-bench.sh` with Bash **inside the WSL distribution being
measured**. This is independent of iKeyd's application and WSL provisioning code.
The script installs missing fio, btrfs-progs, Git and Python packages through apt.
It needs mount/loop privileges, Btrfs kernel support, and ext4 at `/var/tmp`.
Use a quiet, dedicated WSL distribution: `sync` and cache dropping affect other
workloads sharing the kernel. Avoid other disk-intensive work during measurement.

## Start here

From your checkout, update the benchmark branch before entering the root shell:

```bash
git switch bench/wsl-loop-btrfs
git pull --ff-only
```

For reliable WSL runs, first open a dedicated mount namespace. Services activated
in the distribution's normal namespace cannot inherit its benchmark mounts.
This changes only the new shell's namespace, not the distribution's global mount
propagation or service settings. Keep this shell open through measurement,
investigation and cleanup; the following command opens a **root Bash shell**:

```bash
sudo unshare --mount --propagation private bash
```

Inside that shell, from a checkout of `bench/wsl-loop-btrfs`:

```bash
bash tools/wsl-btrfs-bench.sh --smoke
```

The entry point must have LF line endings; `.gitattributes` enforces this even for
Windows checkouts. Do not launch it with PowerShell or `sh`. A password prompt is
announced before preparation. Noninteractive execution without cached sudo
credentials fails with instructions, rather than waiting invisibly. Running the
whole script as root is also supported, but cleanup must use that same user.

`--smoke` runs the same nine conditions (thirteen with `--vhdx`), provisioning, fio, Git, small-file,
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
Each image-backed condition gets a fresh image in every round. Normal mode cleans it up
after that measurement; KEEP_MOUNTS=1 retains the eight loop images (twelve images/disks with `--vhdx`) from the final round.
The free-space check budgets one full image in normal mode (about 26 GiB total
with defaults) or eight in keep mode (about 138 GiB; about 202 GiB with VHDX), plus baseline data and headroom.
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

The script explicitly reports when host space was not checked. It never deletes, formats, resizes or compacts an existing WSL distribution or
its VHDX. VHDX mode creates and removes only its own new benchmark disks. Deleting benchmark images
frees ext4 space but does not promise to shrink the Windows VHDX allocation.

## Conditions and measurement meaning

The default nine-condition matrix is ext4 directly, sparse/fixed-preallocated
loop ext4, sparse-none/LZO/ZSTD Btrfs, and fixed-preallocated-none/LZO/ZSTD Btrfs.
`--vhdx` adds `vhdx-ext4`, `vhdx-none`, `vhdx-lzo`, and `vhdx-zstd`, for thirteen conditions:
Windows drive -> new dynamic VHDX -> WSL virtual disk -> ext4 or Btrfs. It does not put
another raw image/loop underneath the measured VHDX filesystem.
The new `sparse-ext4` and `fixed-ext4` conditions use the same loop/image path,
image capacity, file contents and measurement methods as the Btrfs conditions.
Ext4 mkfs uses `-m 0` and `-E nodiscard,lazy_itable_init=0,lazy_journal_init=0`:
no reserved blocks, no hole punching, and initialization completed before timing.
VHDX ext4 uses the same ext4 formatting options on its owned seed and the same
dynamic VHDX capacity/block size as the three VHDX Btrfs conditions.
Loop and VHDX ext4 use `noatime,nodiscard`, as do the Btrfs mounts; direct ext4 retains
its distribution mount options, recorded in `environment.txt`. ZSTD remains `zstd:3`. Sparse images use
`truncate`; preallocated images use `fallocate`. Btrfs mkfs calls use `-K` to prevent
discard from punching holes in preallocated images. All loop mounts explicitly
request `nodiscard`. Logical size and actual allocated bytes are recorded before
mkfs, after each round's setup and workload; losing preallocation fails the run.
Only newly created, exclusively owned image/loop identities can reach mkfs.

Before every workload the script checks the expected ext4/Btrfs mountpoint, loop backing image or owned VHDX disk,
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

Small-file and Git measurements run in rounds. Direct ext4 runs first, middle and last;
image-backed condition order is reversed/rotated between rounds. `order.tsv` records the actual
sequence. Every round recreates each image-backed filesystem under both cleanup and keep modes;
only the final round is retained by keep mode. Fio data is removed and synced
before the small-file workload to avoid carrying an unrelated large file into it.
Fio runs once per condition in the first round; its one-sample results
cannot quantify run-to-run variance. This ordering reduces, but does not eliminate,
thermal and background-load bias. Comparisons should use matching settings.

## Add the four VHDX conditions

Use **PowerShell run as administrator**, then enter the dedicated distribution.
The Bash entry point remains the same. For example, on the validation machine:

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\Documents\Codex\ikeyd-vhdx-bench"
wsl -d iKeyd-bench -u root -- unshare --mount --propagation private bash
```

Inside that elevated WSL session:

```bash
cd /root/iKeyd
export VHDX_ROOT=/mnt/c/Users/gddro/Documents/Codex/ikeyd-vhdx-bench
HOST_FREE_GIB=30 bash tools/wsl-btrfs-bench.sh --smoke --vhdx
# Full: 300,000 small files x 3 for all 13 conditions; no reduced workload.
HOST_FREE_GIB=30 bash tools/wsl-btrfs-bench.sh --vhdx
```

Replace the username, distribution, checkout path and host budget for your machine.
`VHDX_ROOT` must already exist on a Windows drive mounted under `/mnt/<letter>/`.
The script checks Windows free space before each new VHDX. Full normal mode needs
about 27 GiB of conservative host headroom; keep mode needs substantially more.
Missing qemu-utils is installed by the normal dependency preparation. Windows
interop (`wsl.exe`, `powershell.exe`, `wslpath`) must be available; a Linux-only
PATH remains supported for the nine-condition loop comparison.
VHDX commands invoke the WSL `/init` interpreter with the current session's
Windows token, so loss of the shared `WSLInterop` binfmt registration does not
break attachment or cleanup. This does not elevate a non-administrator session
or change any distribution's interop/systemd configuration. See Microsoft's
[interop implementation](https://github.com/microsoft/WSL/blob/master/doc/docs/technical-documentation/interop.md).


The script creates a private sparse raw seed, verifies its loop ownership, and
formats only that loop. It detaches the seed, converts it with `qemu-img` to a
**dynamic VHDX with 2 MiB blocks**, then attaches the new Windows file using
`wsl.exe --mount --vhd <exact-path> --bare`. It identifies the attached whole disk
by the freshly generated filesystem UUID and size before mounting. It never formats
an attached Windows disk, picks a disk by enumeration order, or uses an existing
user-supplied VHDX. Existing distribution VHDX files are outside this operation.

The log and mappings record the Windows file, Linux device, mount and round.
VHDX file identity uses the Windows drive source, file index and hardlink count.
It does not use drvfs's Linux device number, which can change between Windows
clients. The private directory marker, canonical path, filesystem UUID and disk
size must also match before use or cleanup.
`vhdx_*_host.json` records Windows file length and allocated bytes from
GetCompressedFileSizeW; `storage_*.tsv` records virtual capacity and file length.
`qemu-img info` is collected only before attachment and after detachment.
Dynamic VHDX allocation and ext4 raw-image allocation are different layers; do
not interpret them as interchangeable sparse/preallocated configurations.
The ext4 loop mounts use noatime like Btrfs; direct ext4 retains the distribution's
actual mount options, which are recorded in environment.txt.

`KEEP_MOUNTS=1` also retains final-round VHDX mounts, devices and files. To clean
up, run the same Bash entry point with `--cleanup` from an elevated WSL session,
as the same Linux user and mount namespace. Keep the launching Bash shell open until cleanup. `--vhdx` and `VHDX_ROOT` are not needed for cleanup: the
owned exact paths are journaled. A busy/mismatched mount or ambiguous attachment
fails closed and preserves the affected resources. Windows administrator access
is checked before measurement, so a missing privilege cannot silently skip VHDX.
After a Windows/WSL restart or forced kill, an ambiguous attachment requires
manual inspection of the journal; cleanup does not guess which disk to detach.

[Microsoft WSL disk attachment documentation](https://learn.microsoft.com/en-us/windows/wsl/wsl2-mount-disk)
and [QEMU VHDX options](https://www.qemu.org/docs/master/system/images.html)
describe the host attachment and image format mechanisms.

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
- `vhdx_*.json` (VHDX mode): image metadata and Windows allocated/file bytes.
- `storage_*.tsv`, `usage_*.txt`: logical/physical allocation and filesystem usage
  (Btrfs allocation details or ext4 byte/inode usage).
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

Cleanup verifies ownership and backing identities before unmount/detach. It waits
about five seconds for asynchronous loop detach to become positively absent.
If it remains attached, read-only diagnostics list matching mounts in visible
process mount namespaces. A sandboxed service started during measurement can
inherit a copy of a benchmark mount; unmounting it in the benchmark namespace
alone cannot release that reference. Cleanup preserves the image and requires
inspection of its recorded loop/UUID and that namespace. It never automatically
unmounts other processes' namespaces or stops their services. A busy
mount, unexpected mount/device, pending detach or unjournaled image stops cleanup
with nonzero status and preserves the affected resource. Release investigation
shells/files or extra bind mounts, inspect the log, and rerun `--cleanup`. It never
recursively removes a failed unmount target. Repeat cleanup with no resources is
safe. A lock excludes concurrent run/cleanup by the same user. Run cleanup in the
same dedicated mount-namespace shell: another terminal cannot see those mounts.
To investigate from another terminal, first enter the original shell's namespace
with `sudo nsenter --mount=/proc/<shell-pid>/ns/mnt bash`; `echo $$` in the
original interactive shell shows its PID. Close that shell only after cleanup.

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

### Expanded matrix validation

The 13-condition keep-mode smoke (including sparse/preallocated loop ext4 and
VHDX ext4/none/LZO/ZSTD) completed in **197 seconds**, exit **0**, run ID
`20260915-113447-iYwMp4`. It produced 39 small-file JSONs (1,000 files each),
39 Git JSONs and 91 successful fio JSONs. All twelve image-backed filesystems
were retained with verified mount types/UUIDs and compression options.
A busy VHDX blocked cleanup with exit 1 and preserved that mount; releasing it
allowed cleanup and repeated cleanup to return 0. These used the actual WSL
disk attachment path, not mocks.

Expanded validation found two host lifecycle issues. Services launched in the
normal mount namespace could inherit loop mounts, preventing final detach;
the documented dedicated namespace avoids that inheritance. Windows interop's
binfmt registration could disappear, breaking ordinary PE execution; the
VHDX helper now invokes the WSL interpreter using the current client's token.
Drive identity was also corrected to use the drive source/file index instead
of the client-dependent drvfs device number. Stale device records after a WSL
restart were manually reconciled using exact saved UUIDs, sizes and file indices;
automatic cleanup intentionally refused the mismatch before that inspection.

The expanded full run used `HOST_FREE_GIB=30 bash tools/wsl-btrfs-bench.sh --vhdx`
inside that elevated dedicated namespace. It completed in **2,989 seconds
(49 min 49 sec)** with exit **0**, run ID `20260915-113907-6h9UXF`.
All thirteen conditions recorded 300,000 files x 3 (11.7 million file creations
total), 15,000 Git source files x 3, and the default 1 GiB/15-second fio jobs.
Validation found 39 small-file JSONs, 39 Git JSONs, 91 fio JSONs with no fio errors,
and 104 summary rows. Baseline positions were 1, 7 and 13 across the three rounds.
Every preallocated image retained its full 16 GiB allocation after mkfs and
workloads; all 24 VHDX host-allocation records were nonzero. Final cleanup removed
the owned work/mount directories, loops and new VHDX files.

Small-file median seconds were: direct ext4 15.04, sparse loop ext4 13.25,
preallocated loop ext4 13.54, and VHDX ext4 13.81. VHDX Btrfs medians were
21.35 (none), 29.66 (LZO), and 43.00 (ZSTD:3). These are the measured complete
create/stat/read/rename/sync/delete cycle, not creation alone. Ext4 ranges were
9.24-15.58, 10.07-35.35, 8.61-38.69 and 13.38-40.03 seconds respectively.
The variation is substantial: this run does not establish a stable ranking among
the four ext4 layouts. Fio has one sample per job and includes cache-layer effects.

Full-size simultaneous retention remains untested because of host capacity;
the twelve mounts were retained together at smoke size. Signal handling was
tested on the earlier seven-condition path; no separate VHDX Ctrl+C test was run.
No automated regression/test files were added.

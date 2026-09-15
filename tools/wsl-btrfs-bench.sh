#!/usr/bin/env bash
set -Eeuo pipefail
umask 077
export LC_ALL=C

# Bash entry point for the WSL direct/loop ext4 and loop/VHDX Btrfs comparison.
# See docs/wsl-btrfs-bench.md. --smoke changes sizes, never the processing path.
MODE=full
ACTION=run
INCLUDE_VHDX=0
for arg in "$@"; do
  case "$arg" in
    --smoke) MODE=smoke ;;
    --cleanup) ACTION=cleanup ;;
    --vhdx) INCLUDE_VHDX=1 ;;
    *) echo "Usage: bash $0 [--smoke] [--vhdx] | --cleanup" >&2; exit 2 ;;
  esac
done
if [[ "$MODE" == smoke ]]; then
  RUNTIME=1 IMAGE_GIB=1 FIO_SIZE=16M SMALL_FILES=1000 SMALL_RUNS=3 GIT_FILES=100 GIT_RUNS=3
else
  RUNTIME="${RUNTIME:-15}" IMAGE_GIB="${IMAGE_GIB:-16}" FIO_SIZE="${FIO_SIZE:-1G}"
  SMALL_FILES="${SMALL_FILES:-300000}" SMALL_RUNS="${SMALL_RUNS:-3}"
  GIT_FILES="${GIT_FILES:-15000}" GIT_RUNS="${GIT_RUNS:-3}"
fi
COMPRESS_PCT="${COMPRESS_PCT:-60}"
DROP_CACHES="${DROP_CACHES:-1}"
KEEP_MOUNTS="${KEEP_MOUNTS:-0}"
WORK="/var/tmp/wsl-btrfs-bench-${UID}"
BASE="$WORK/ext4"
MNT_ROOT="/mnt/wsl-btrfs-bench-${UID}"
SRC="$WORK/git-source"
SCRIPT="$(readlink -f -- "$0")"
VARIANTS=(ext4 sparse-ext4 fixed-ext4 sparse-none sparse-lzo sparse-zstd fixed-none fixed-lzo fixed-zstd)
ALL_VARIANTS=("${VARIANTS[@]}" vhdx-ext4 vhdx-none vhdx-lzo vhdx-zstd)
if [[ "$INCLUDE_VHDX" == 1 ]]; then VARIANTS=("${ALL_VARIANTS[@]}"); fi
COUNT=${#VARIANTS[@]}
RESULTS="$(mktemp -d "${HOME}/wsl-btrfs-bench-results-$(date +%Y%m%d-%H%M%S)-XXXXXX")"
LOG="$RESULTS/run.log"
STAGE=startup
START=$SECONDS
OWNED=0
HEARTBEAT=''
exec > >(trap '' INT TERM; exec tee -a "$LOG") 2>&1

say() { printf '[%s +%ss] %s\n' "$(date -Is)" "$((SECONDS-START))" "$*"; }
die() { say "ERROR [$STAGE] $*" >&2; exit 1; }
root() { if (( EUID == 0 )); then "$@"; else sudo -n -- "$@"; fi; }
begin() {
  STAGE="$*"
  printf '%s' "$STAGE" > "$RESULTS/current-stage"
  say "START $STAGE"
}
step() {
  begin "$1"
  shift
  local started=$SECONDS
  # Keep this call out of conditionals: otherwise Bash disables errexit in functions.
  "$@"
  say "END $STAGE elapsed=$((SECONDS-started))s"
}
error() {
  local rc=$1 line=$2 command=$3
  say "ERROR [$STAGE] exit=$rc line=$line command=$command; log=$LOG" >&2
  return "$rc"
}
trap 'error "$?" "$LINENO" "$BASH_COMMAND"' ERR
trap 'say "Interrupted [$STAGE]"; exit 130' INT
trap 'say "Terminated [$STAGE]"; exit 143' TERM

# The manifest is data, never shell code. Paths are derived from fixed variant names.
# A failed or interrupted operation leaves enough identity for --cleanup to verify.
validate_work() {
  [[ -d "$WORK" && ! -L "$WORK" && $(stat -c %u "$WORK") == "$UID" && $(stat -c %a "$WORK") == 700 ]] || return 1
  [[ -f "$WORK/owner" && ! -L "$WORK/owner" && $(cat "$WORK/owner") == "wsl-btrfs-bench-v2:$UID" ]] || return 1
  [[ ! -L "$MNT_ROOT" ]] || return 1
}
image_identity() { stat -c '%d:%i:%s:%h' -- "$1"; }
check_loop() {
  local loop=$1 img=$2 expected=$3 backing attached
  [[ "$loop" =~ ^/dev/loop[0-9]+$ && -b "$loop" && -f "$img" && ! -L "$img" ]] || return 1
  [[ $(image_identity "$img") == "$expected" && "$expected" == *:1 ]] || return 1
  backing="$(root losetup --noheadings --raw --output BACK-FILE "$loop")" || return 1
  [[ "$backing" == "$img" ]] || return 1
  attached="$(root losetup -j "$img" --noheadings --output NAME)" || return 1
  [[ "$attached" == "$loop" ]] || return 1
}
wait_loop_detached() {
  local loop=$1 img=$2 attached attempt
  # LOOP_CLR_FD may complete asynchronously while udev releases its references.
  # Wait for positive absence; never delete an image merely because detach ran.
  for ((attempt=0; attempt<50; attempt++)); do
    attached="$(root losetup -j "$img" --noheadings --output NAME)" || return 1
    [[ -n "$attached" ]] || return 0
    [[ "$attached" == "$loop" ]] || return 1
    sleep 0.1
  done
  say "Loop detach still pending after 5s: $loop; preserving image"
  # Read-only diagnostics: do not unmount another process's namespace automatically.
  root python3 - "$loop" <<'PYDIAG' || say 'Namespace diagnostics unavailable'
import pathlib, sys
seen = set()
for p in pathlib.Path('/proc').glob('[0-9]*'):
    try:
        ns = str((p/'ns/mnt').readlink())
        if ns in seen: continue
        seen.add(ns)
        for line in (p/'mountinfo').read_text().splitlines():
            left, right = line.split(' - ', 1)
            if right.split()[1] == sys.argv[1]:
                print(f'Loop mount retained: pid={p.name} namespace={ns} mountpoint={left.split()[4]}', flush=True)
    except (FileNotFoundError, PermissionError, ProcessLookupError):
        continue
PYDIAG
  return 1
}
no_mounts_under() {
  # Include bind mounts, descendants, and exact mountpoints; do not infer absence
  # from findmnt failure. Fixed paths contain no mountinfo escaping characters.
  awk -v p="$1" '$5 == p || index($5,p "/") == 1 {found=1} END {exit found ? 1 : 0}' /proc/self/mountinfo
}
cleanup_resources() {
  validate_work || { say "Refusing cleanup: missing/invalid ownership marker at $WORK (legacy data requires manual inspection)"; return 1; }
  local v img mnt loop expected attached source fs uuid saved_uuid
  local selected=("${ALL_VARIANTS[@]:1}")
  [[ "$1" == all ]] || selected=("$1")
  for v in "${selected[@]}"; do
    if [[ "$v" == vhdx-* ]]; then cleanup_vhdx "$v" || return 1; continue; fi
    img="$WORK/$v.img"; mnt="$MNT_ROOT/$v"
    [[ ! -L "$mnt" && ! -L "$img" && ! -L "$WORK/$v.state" ]] || return 1
    if [[ -f "$WORK/$v.state" ]]; then
      read -r loop expected < "$WORK/$v.state" || return 1
      if [[ -e "$img" ]]; then
        [[ $(image_identity "$img") == "$expected" ]] || { say "Image identity changed: $img"; return 1; }
        attached="$(root losetup -j "$img" --noheadings --output NAME)" || return 1
        if [[ -n "$attached" ]]; then
          if [[ "$attached" != "$loop" ]] || ! check_loop "$loop" "$img" "$expected"; then say "Loop ownership mismatch: $img"; return 1; fi
          if mountpoint -q -- "$mnt"; then
            read -r source fs uuid < <(findmnt -rn -M "$mnt" -o SOURCE,FSTYPE,UUID)
            saved_uuid="$(cat "$WORK/$v.uuid")" || return 1
            [[ "$source" == "$loop" && "$fs" == "$(variant_fs "$v")" && "$uuid" == "$saved_uuid" ]] || { say "Mount identity mismatch: $mnt"; return 1; }
            root umount -- "$mnt" || { say "Unmount failed: preserving mount, loop, image and work directory: $mnt"; return 1; }
          fi
          no_mounts_under "$mnt" || return 1
          # A mount elsewhere (including an investigation bind mount) prevents detach.
          if findmnt -rn -S "$loop" >/dev/null; then say "Loop still mounted elsewhere: $loop"; return 1; fi
          root losetup -d "$loop" || return 1
          wait_loop_detached "$loop" "$img" || return 1
        fi
      else
        # Never detach a reused device based only on an old device number.
        no_mounts_under "$mnt" || return 1
      fi
    elif [[ -e "$img" ]]; then
      # Crash between creation/attachment and journal write: preserve for recovery.
      say "Unjournaled image: $img; inspect manually; nothing will be deleted"
      return 1
    fi
    no_mounts_under "$mnt" || return 1
    [[ ! -d "$mnt" ]] || root rmdir -- "$mnt" || return 1
    rm -f -- "$img" "$WORK/$v.state" "$WORK/$v.uuid" || return 1
  done
  [[ "$1" == all ]] || return 0
  no_mounts_under "$MNT_ROOT" && no_mounts_under "$WORK" || return 1
  [[ ! -d "$MNT_ROOT" ]] || root rmdir -- "$MNT_ROOT" || return 1
  # No recursive deletion of mount directories; refuse any remaining work mounts.
  rm -rf --one-file-system -- "$WORK" || return 1
  say "Cleanup complete; results retained at $RESULTS"
}
cleanup_all() { cleanup_resources all; }
print_preserved() {
  say "Preserved resources (also on failure/interrupt when KEEP_MOUNTS=1):"
  say "Historical mount mappings (round column; only still-journaled entries are retained):"
  [[ ! -f "$WORK/mounts.tsv" ]] || cat "$WORK/mounts.tsv"
  local v
  for v in "${ALL_VARIANTS[@]:1}"; do
    [[ ! -f "$WORK/$v.state" ]] || printf 'Retained %s: %s\n' "$v" "$(cat "$WORK/$v.state")"
  done
  for v in vhdx-ext4 vhdx-none vhdx-lzo vhdx-zstd; do
    [[ ! -f "$WORK/$v.vhdx-dir" ]] || printf 'Retained %s VHDX directory: %s\n' "$v" "$(cat "$WORK/$v.vhdx-dir")"
  done
  say "Ownership/recovery records: $WORK"
  printf 'Cleanup as the same user in this distro: bash %q --cleanup\n' "$SCRIPT"
}
on_exit() {
  local rc=$?
  trap - EXIT ERR INT TERM
  if [[ -n "$HEARTBEAT" ]]; then kill "$HEARTBEAT" 2>/dev/null || true; wait "$HEARTBEAT" 2>/dev/null || true; fi
  if (( OWNED )); then
    if [[ "$KEEP_MOUNTS" == 1 ]]; then
      print_preserved
    elif ! cleanup_all; then
      say "ERROR cleanup failed; resources preserved at $WORK; rerun --cleanup after resolving the error"
      (( rc != 0 )) || rc=1
    fi
  fi
  printf '%s\n' "$rc" > "$RESULTS/exit-code"
  if (( rc == 0 )); then say "SUCCESS ($ACTION/$MODE); results=$RESULTS";
  else say "FAILED [$STAGE] exit=$rc; partial results=$RESULTS; log=$LOG"; fi
  exit "$rc"
}
trap on_exit EXIT
variant_fs() { if [[ "$1" == *-ext4 ]]; then echo ext4; else echo btrfs; fi; }
# shellcheck source=tools/wsl-btrfs-bench-vhdx.sh
source "$(dirname "$SCRIPT")/wsl-btrfs-bench-vhdx.sh"

install_deps() {
  local pkgs=() cmd
  command -v fio >/dev/null || pkgs+=(fio)
  command -v mkfs.btrfs >/dev/null || pkgs+=(btrfs-progs)
  command -v mkfs.ext4 >/dev/null || pkgs+=(e2fsprogs)
  if [[ "$INCLUDE_VHDX" == 1 ]]; then command -v qemu-img >/dev/null || pkgs+=(qemu-utils); fi
  command -v git >/dev/null || pkgs+=(git)
  command -v python3 >/dev/null || pkgs+=(python3)
  if ((${#pkgs[@]})); then
    say "Installing: ${pkgs[*]} (apt output follows; network/package locks can take time)"
    root apt-get update
    root env DEBIAN_FRONTEND=noninteractive apt-get install -y "${pkgs[@]}"
  fi
  for cmd in fio mkfs.ext4 mkfs.btrfs btrfs git python3 losetup fallocate truncate findmnt mountpoint flock timeout stat sync; do
    command -v "$cmd" >/dev/null || die "Missing required command: $cmd"
  done
  [[ $(fio --version) == fio-* ]] || die 'fio must be the Flexible I/O Tester'
}
check_environment() {
  [[ $(uname -s) == Linux ]] || die 'Run inside WSL/Linux'
  [[ $(findmnt -rn -T /var/tmp -o FSTYPE) == ext4 ]] || die '/var/tmp must be on ext4, not a Windows drive'
  if ! grep -qw btrfs /proc/filesystems; then root modprobe btrfs; fi
  grep -qw btrfs /proc/filesystems || die 'Btrfs is unavailable in this kernel'
  local n fio_bytes image_bytes working_bytes need free inodes image_count=1
  for n in RUNTIME IMAGE_GIB SMALL_FILES SMALL_RUNS GIT_FILES GIT_RUNS; do
    [[ ${!n} =~ ^[1-9][0-9]{0,6}$ ]] || die "$n must be a positive integer (at most seven digits)"
  done
  [[ "$COMPRESS_PCT" =~ ^[0-9]{1,2}$|^100$ ]] || die 'COMPRESS_PCT must be 0..100'
  [[ "$KEEP_MOUNTS" =~ ^[01]$ && "$DROP_CACHES" =~ ^[01]$ ]] || die 'KEEP_MOUNTS and DROP_CACHES must be 0 or 1'
  if [[ "$MODE" == full ]]; then
    [[ "$SMALL_FILES" == 300000 && "$SMALL_RUNS" == 3 ]] || die 'Full measurement requires 300000 files x 3; use --smoke for a short check'
  fi
  [[ "$FIO_SIZE" =~ ^[1-9][0-9]{0,6}[KMG]$ ]] || die 'FIO_SIZE must be an integer with K, M or G suffix'
  fio_bytes=$(numfmt --from=iec "$FIO_SIZE")
  image_bytes=$((IMAGE_GIB*1024*1024*1024))
  # Conservative simultaneous per-filesystem high-water mark, including metadata.
  # Budget Btrfs metadata DUP, CoW and block-group reservations, not just payload.
  working_bytes=$((fio_bytes + SMALL_FILES*24576 + GIT_FILES*8192 + 512*1024*1024))
  (( image_bytes > working_bytes )) || die "Image too small: need >$working_bytes bytes per image including metadata/headroom"
  # Each round starts with a fresh image. Keep only the last round on request.
  [[ "$KEEP_MOUNTS" != 1 ]] || image_count=$((COUNT-1))
  need=$((image_count*image_bytes + working_bytes + 1024*1024*1024))
  # Conversion also keeps a sparse mkfs-only raw seed, without benchmark data.
  if [[ "$INCLUDE_VHDX" == 1 ]]; then need=$((need + 512*1024*1024)); fi
  free=$(df -B1 --output=avail /var/tmp | tail -n 1)
  inodes=$(df --output=iavail /var/tmp | tail -n 1)
  say "Capacity: ext4 free=$free required=$need bytes; free inodes=$inodes"
  (( free >= need )) || die "Insufficient ext4 capacity: require $need bytes; use a smaller IMAGE_GIB only if it still fits the workload"
  (( inodes > SMALL_FILES + GIT_FILES*2 + 10000 )) || die 'Insufficient ext4 inodes'
  if [[ -n "${HOST_FREE_GIB:-}" ]]; then
    [[ "$HOST_FREE_GIB" =~ ^[1-9][0-9]{0,6}$ ]] || die 'HOST_FREE_GIB must be a positive integer'
    (( HOST_FREE_GIB*1024*1024*1024 >= need )) || die "Insufficient Windows host capacity: need $need bytes; HOST_FREE_GIB=$HOST_FREE_GIB"
  else
    say 'Windows VHDX host free space is not inferable from ext4 df. Check its host drive; set HOST_FREE_GIB to enforce that budget.'
  fi
  if [[ "$INCLUDE_VHDX" == 1 ]]; then check_vhdx_environment; fi
  [[ ! -e "$WORK" && ! -L "$WORK" ]] || die "Existing work retained untouched: $WORK; inspect, then use --cleanup as the same user"
  [[ ! -e "$MNT_ROOT" && ! -L "$MNT_ROOT" ]] || die "Existing mount directory retained untouched: $MNT_ROOT"
}
capture_env() {
  {
    date -Is; uname -a
    findmnt -rn -T /var/tmp -o SOURCE,FSTYPE,OPTIONS
    fio --version; btrfs --version; git --version
    printf '%s\n' "mode=$MODE" "image_gib=$IMAGE_GIB" "fio_size=$FIO_SIZE" "runtime=$RUNTIME" "small_files=$SMALL_FILES" "small_runs=$SMALL_RUNS" "git_files=$GIT_FILES" "git_runs=$GIT_RUNS" "compress_pct=$COMPRESS_PCT" "drop_caches=$DROP_CACHES" "keep_mounts=$KEEP_MOUNTS" "host_free_gib=${HOST_FREE_GIB:-unchecked}" "wsl_distro=${WSL_DISTRO_NAME:-unknown}"
    echo 'fio_seed=20260915; mkfs discard disabled; mount nodiscard; Git source on ext4'
    mkfs.ext4 -V 2>&1
    printf '%s\n' "variants=${VARIANTS[*]}" "mount_namespace=$(readlink /proc/self/ns/mnt)"
    if [[ "$INCLUDE_VHDX" == 1 ]]; then qemu-img --version; printf 'vhdx_root=%s\n' "$VHDX_ROOT"; fi
    if command -v wsl.exe >/dev/null 2>&1; then
      echo '--- optional wsl.exe --version (10s limit) ---'
      timeout --kill-after=2s 10s wsl.exe --version 2>&1 | decode_windows_output || echo 'Optional WSL version capture failed/timed out'
    else
      echo 'Optional wsl.exe absent from PATH; continuing'
    fi
  } > "$RESULTS/environment.txt"
  return 0
}
cache_reset() {
  sync
  if [[ "$DROP_CACHES" == 1 ]]; then
    root sh -c 'echo 3 > /proc/sys/vm/drop_caches'
  fi
}
prepare_git_repo() {
  mkdir "$SRC"
  git -C "$SRC" init -q
  git -C "$SRC" config user.email bench@example.invalid
  git -C "$SRC" config user.name Bench
  git -C "$SRC" config gc.auto 0
  git -C "$SRC" config maintenance.auto false
  python3 - "$SRC" "$GIT_FILES" <<'PY'
import os, sys
root, n = sys.argv[1], int(sys.argv[2])
body = 'class Example { static final String VALUE = "0123456789abcdef"; }\n' * 16
for i in range(n):
    d = os.path.join(root, f'src/pkg{i//250:04d}')
    os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, f'file{i:07d}.txt'), 'w') as f:
        f.write(f'{i}\n{body}')
PY
  git -C "$SRC" add -A
  git -C "$SRC" commit -q -m bench
  git -C "$SRC" gc --prune=now
  git -C "$SRC" rev-parse HEAD > "$RESULTS/git-source-commit"
}
setup_variant() {
  local v=$1 round=$2 img="$WORK/$1.img" mnt="$MNT_ROOT/$1" loop identity uuid opts
  [[ "$v" != ext4 ]] || { mkdir "$BASE"; return 0; }
  if [[ "$v" == vhdx-* ]]; then setup_vhdx "$v" "$round"; return 0; fi
  # Noclobber + private fresh work directory: never format an existing image.
  (set -C; : > "$img")
  if [[ "$v" == sparse-* ]]; then truncate -s "${IMAGE_GIB}G" "$img";
  else fallocate -l "${IMAGE_GIB}G" "$img"; fi
  identity=$(image_identity "$img")
  printf 'round_%s_logical_bytes\t%s\nround_%s_allocated_before_mkfs\t%s\n' "$round" "$(stat -c %s "$img")" "$round" "$(( $(stat -c %b "$img")*512 ))" >> "$RESULTS/storage_$v.tsv"
  loop=$(root losetup --find --show --nooverlap "$img")
  # Journal immediately, before mkfs/mount or any other fallible validation.
  printf '%s %s\n' "$loop" "$identity" > "$WORK/$v.state"
  sync -f "$WORK/$v.state"
  check_loop "$loop" "$img" "$identity" || die "New loop ownership validation failed: $loop"
  if [[ "$(variant_fs "$v")" == ext4 ]]; then
    root mkfs.ext4 -q -m 0 -E nodiscard,lazy_itable_init=0,lazy_journal_init=0 "$loop"
  else root mkfs.btrfs -q -K -L "bench-$v" "$loop"; fi
  uuid=$(root blkid -s UUID -o value "$loop")
  [[ -n "$uuid" ]] || die "No UUID on new filesystem $loop"
  printf '%s\n' "$uuid" > "$WORK/$v.uuid"
  sync -f "$WORK/$v.uuid"
  # mkfs discard is disabled to preserve preallocation; nodiscard prevents subsequent hole punching.
  opts=noatime,nodiscard
  case "$v" in *-lzo) opts+=,compress=lzo ;; *-zstd) opts+=,compress=zstd:3 ;; esac
  root mkdir "$mnt"
  root mount -t "$(variant_fs "$v")" -o "$opts" "$loop" "$mnt"
  printf '%s\t%s\t%s\t%s\t%s\n' "$v" "$mnt" "$img" "$loop" "$round" >> "$WORK/mounts.tsv"
  cp "$WORK/mounts.tsv" "$RESULTS/mounts.tsv"
  assert_mount "$v"
  root chown "$(id -u):$(id -g)" "$mnt"
  record_storage "$v" "setup_round_$round"
}
assert_mount() {
  local v=$1 dir source fs uuid loop expected saved_uuid opts
  if [[ "$v" == ext4 ]]; then
    [[ $(findmnt -rn -T "$BASE" -o FSTYPE) == ext4 ]] || die 'Baseline no longer on ext4'
    return 0
  fi
  dir="$MNT_ROOT/$v"
  if [[ "$v" == vhdx-* ]]; then
    check_vhdx_device "$v" || die "VHDX device ownership changed: $v"
    loop=$(cat "$WORK/$v.device")
  else
  read -r loop expected < "$WORK/$v.state"
  check_loop "$loop" "$WORK/$v.img" "$expected" || die "Loop ownership changed: $v"
  fi
  if ! read -r source fs uuid opts < <(findmnt -rn -M "$dir" -o SOURCE,FSTYPE,UUID,OPTIONS); then die "Expected benchmark mount absent: $dir"; fi
  saved_uuid=$(cat "$WORK/$v.uuid")
  [[ "$source" == "$loop" && "$fs" == "$(variant_fs "$v")" && "$uuid" == "$saved_uuid" ]] || die "Expected benchmark mount absent or wrong: $dir"
  [[ ! "$opts" =~ (^|,)discard(=|,|$) ]] || die "Discard unexpectedly enabled: $dir"
  case "$v" in
    *-none) [[ "$opts" != *compress* ]] || die "Unexpected compression: $opts" ;;
    *-lzo) [[ ",$opts," == *,compress=lzo,* ]] || die "LZO not active: $opts" ;;
    *-zstd) [[ ",$opts," == *,compress=zstd:3,* ]] || die "ZSTD:3 not active: $opts" ;;
  esac
  printf '%s\t%s\t%s\t%s\n' "$(date -Is)" "$v" "$source" "$opts" >> "$RESULTS/mount-checks.tsv"
}
record_storage() {
  local v=$1 phase=$2 allocated logical
  [[ "$v" != ext4 ]] || return 0
  sync
  if [[ "$v" == vhdx-* ]]; then record_vhdx_storage "$v" "$phase"; return 0; fi
  allocated=$(( $(stat -c %b "$WORK/$v.img")*512 ))
  logical=$(stat -c %s "$WORK/$v.img")
  printf 'allocated_%s\t%s\n' "$phase" "$allocated" >> "$RESULTS/storage_$v.tsv"
  if [[ "$v" == fixed-* ]]; then (( allocated >= logical )) || die "Preallocation lost: $v allocated=$allocated logical=$logical"; fi
  if [[ "$(variant_fs "$v")" == btrfs ]]; then
    root btrfs filesystem usage -b "$MNT_ROOT/$v" > "$RESULTS/usage_${v}_${phase}.txt"
  else
    df -B1 "$MNT_ROOT/$v" > "$RESULTS/usage_${v}_${phase}.txt"
    df -i "$MNT_ROOT/$v" >> "$RESULTS/usage_${v}_${phase}.txt"
  fi
}
fio_one() {
  local v=$1 dir=$2 test=$3 rw=$4 bs=$5 direct=$6 fsync=$7 cache=$8
  assert_mount "$v"
  if [[ "$cache" != warm ]]; then cache_reset; fi
  local args=(--name="$test" --filename="$dir/fio.bin" --size="$FIO_SIZE"
    --rw="$rw" --bs="$bs" --ioengine=sync --iodepth=1 --numjobs=1
    --direct="$direct" --time_based=1 --runtime="$RUNTIME" --ramp_time=0
    --randrepeat=1 --randseed=20260915 --group_reporting=1
    --output-format=json --output="$RESULTS/fio_${v}_${test}.json")
  if [[ "$rw" == *write* ]]; then args+=(--buffer_compress_percentage="$COMPRESS_PCT" --buffer_compress_chunk=4k --end_fsync=1); fi
  [[ -z "$fsync" ]] || args+=(--fsync="$fsync")
  printf '%s\t%s\t%s\t%s\t%s\n' "$v" "$test" "$direct" "$cache" "$DROP_CACHES" >> "$RESULTS/cache.tsv"
  fio "${args[@]}"
  sync
}
run_fio_suite() {
  local v=$1 dir=$2 cold=cold
  [[ "$DROP_CACHES" == 1 ]] || cold=uncontrolled
  # Fully initialize identical-length data before reads, independent of runtime.
  step "[$v] fio data initialization" fio --name=initialize --filename="$dir/fio.bin" --size="$FIO_SIZE" --rw=write --bs=1M --ioengine=sync --direct=0 --buffer_compress_percentage="$COMPRESS_PCT" --buffer_compress_chunk=4k --randseed=20260915 --end_fsync=1 --output-format=json --output="$RESULTS/fio_${v}_initialize.json"
  step "[$v] fio seq-write-$cold (buffered)" fio_one "$v" "$dir" "seq-write-$cold" write 1M 0 '' "$cold"
  # Explicitly touch the entire file; no cache drop between this and warm write.
  step "[$v] warm cache preparation" dd if="$dir/fio.bin" of=/dev/null bs=1M status=none
  step "[$v] fio seq-write-warm (buffered)" fio_one "$v" "$dir" seq-write-warm write 1M 0 '' warm
  step "[$v] fio seq-read (direct)" fio_one "$v" "$dir" seq-read read 1M 1 '' "$cold"
  step "[$v] fio rand-read (direct)" fio_one "$v" "$dir" rand-read randread 4k 1 '' "$cold"
  step "[$v] fio rand-write (buffered)" fio_one "$v" "$dir" rand-write randwrite 4k 0 '' "$cold"
  step "[$v] fio fsync-write (buffered, fsync every write)" fio_one "$v" "$dir" fsync-write randwrite 4k 0 1 "$cold"
  step "[$v] release fio data before metadata workload" rm -- "$dir/fio.bin"
  sync
}
smallfiles_one() {
  local v=$1 dir=$2 run=$3
  assert_mount "$v"
  cache_reset
  python3 - "$dir/smallfiles-$run" "$SMALL_FILES" "$RESULTS/smallfiles_${v}_${run}.json" <<'PY'
import json, os, shutil, sys, time
root, n, output = sys.argv[1], int(sys.argv[2]), sys.argv[3]
os.mkdir(root)
payload = (b'int value = 123456789; // benchmark source-like payload\n' * 20)[:1024]
result = {'files': n, 'payload_bytes': len(payload), 'phases_seconds': {}}
start = time.perf_counter()
def phase(name, fn):
    t = time.perf_counter()
    fn()
    result['phases_seconds'][name] = time.perf_counter()-t
    print(f'  small-files {name} done: {result["phases_seconds"][name]:.3f}s', flush=True)
def path(i):
    return os.path.join(root, f'd{i//1000:04d}', f'f{i:07d}.txt')
def create():
    for i in range(n):
        if i % 1000 == 0: os.mkdir(os.path.dirname(path(i)))
        with open(path(i), 'wb', buffering=0) as f: f.write(payload)
def stat_all():
    for i in range(n):
        if os.stat(path(i)).st_size != len(payload): raise RuntimeError('Size mismatch')
def read_all():
    for i in range(n):
        with open(path(i), 'rb') as f:
            if f.read() != payload: raise RuntimeError('Payload mismatch')
def rename():
    for i in range(0, n, 2): os.rename(path(i), path(i)+'.renamed')
phase('create', create)
phase('stat', stat_all)
phase('read', read_all)
phase('rename', rename)
phase('sync', os.sync)
phase('delete', lambda: shutil.rmtree(root))
phase('sync_after_delete', os.sync)
result['seconds'] = time.perf_counter()-start
with open(output, 'x') as f: json.dump(result, f, indent=2)
PY
}
git_one() {
  local v=$1 dir=$2 run=$3
  assert_mount "$v"
  cache_reset
  python3 - "$SRC" "$dir/git-clone-$run" "$RESULTS/git_${v}_${run}.json" <<'PY'
import json, shutil, subprocess, sys, time
src, dest, output = sys.argv[1:]
t = time.perf_counter()
subprocess.run(['git', 'clone', '-q', '--no-local', 'file://'+src, dest], check=True)
clone = time.perf_counter()-t
t = time.perf_counter()
subprocess.run(['git', '-C', dest, 'status', '--porcelain'], check=True, stdout=subprocess.DEVNULL)
status = time.perf_counter()-t
with open(output, 'x') as f: json.dump({'seconds': clone, 'status_seconds': status}, f)
shutil.rmtree(dest)
PY
  sync
}
summary() {
  python3 - "$RESULTS" "$SMALL_FILES" "$SMALL_RUNS" "$GIT_RUNS" "$DROP_CACHES" "$MODE" "${VARIANTS[@]}" <<'PY'
import csv, json, math, os, statistics, sys
r, files, runs, git_runs, drop, mode = sys.argv[1:7]
vs = sys.argv[7:]
def read(name):
    with open(os.path.join(r,name)) as f: return json.load(f)
def positive(x):
    x=float(x)
    if not math.isfinite(x) or x <= 0: raise ValueError(f'Empty/invalid measurement: {x}')
    return x
rows=[]
for v in vs:
    for name, count in [('smallfiles',int(runs)), ('git',int(git_runs))]:
        values=[]
        for i in range(1,count+1):
            data=read(f'{name}_{v}_{i}.json')
            if name == 'smallfiles' and data['files'] != int(files): raise ValueError('Wrong file count')
            values.append(positive(data['seconds']))
        rows.append([v,name,'seconds',statistics.median(values),min(values),max(values),statistics.pstdev(values),','.join(map(str,values))])
    cold='cold' if drop == '1' else 'uncontrolled'
    for test,kind,field,unit in [(f'seq-write-{cold}','write','bw_bytes','bytes/s'),('seq-write-warm','write','bw_bytes','bytes/s'),('seq-read','read','bw_bytes','bytes/s'),('rand-read','read','iops','IOPS'),('rand-write','write','iops','IOPS'),('fsync-write','write','iops','IOPS')]:
        data=read(f'fio_{v}_{test}.json')
        if len(data['jobs']) != 1 or data['jobs'][0]['error'] != 0: raise ValueError('fio failed')
        job=data['jobs'][0]
        positive(job[kind]['io_bytes'])
        x=positive(job[kind][field])
        rows.append([v,test,unit,x,x,x,0,str(x)])
with open(os.path.join(r,'summary.tsv'),'x') as f:
    w=csv.writer(f,delimiter='\t'); w.writerow(['variant','test','unit','median','min','max','population_stddev','runs']); w.writerows(rows)
lines=[f'WSL direct/loop ext4 and loop/VHDX Btrfs comparison ({mode})',f'Small files: {files} x {runs} per condition; Git: {git_runs} runs', f'All {len(vs)} conditions validated; raw results and execution order retained.', 'Values: median [min, max], population standard deviation']
for v,test,unit,med,lo,hi,sd,_ in rows:
    lines.append(f'{v:12} {test:23} {med:.3f} [{lo:.3f}, {hi:.3f}] sd={sd:.3f} {unit}')
text='\n'.join(lines)+'\n'
with open(os.path.join(r,'summary.txt'),'x') as f: f.write(text)
print(text)
PY
}
main() {
  say "WSL direct/loop ext4 and loop/VHDX Btrfs: action=$ACTION mode=$MODE"
  say "$COUNT conditions; image=${IMAGE_GIB}GiB (one active, $((COUNT-1)) retained only with KEEP_MOUNTS=1); small files=$SMALL_FILES x$SMALL_RUNS; Git=$GIT_FILES x$GIT_RUNS; fio=$FIO_SIZE/${RUNTIME}s; DROP_CACHES=$DROP_CACHES KEEP_MOUNTS=$KEEP_MOUNTS"
  say "Work=$WORK; mounts=$MNT_ROOT; results=$RESULTS; log=$LOG"
  say 'Preallocated images consume actual ext4 space; VHDX host allocation may remain after cleanup.'
  begin '[prepare] sudo authentication (enter password here if requested)'
  if (( EUID != 0 )); then
    if ! sudo -n true; then
      [[ -t 0 ]] || die 'sudo needs a password: run sudo -v in an interactive WSL terminal, then rerun'
      sudo -v
    fi
  fi
  # Stable user-owned lock outside WORK, retained across cleanup/recreation.
  local lock="${HOME}/.wsl-btrfs-bench.lock"
  [[ ! -L "$lock" ]] || die "Refusing symlink lock: $lock"
  exec 9> "$lock"
  flock -n 9 || die 'Another benchmark/cleanup for this user is running'
  (
    trap - EXIT ERR INT TERM
    while sleep 15; do
      say "RUNNING $(cat "$RESULTS/current-stage") (elapsed; details: $LOG)"
      if (( EUID != 0 )); then sudo -n -v || exit 1; fi
    done
  ) 9>&- &
  HEARTBEAT=$!
  if [[ "$ACTION" == cleanup ]]; then
    if [[ ! -e "$WORK" && ! -L "$WORK" && ! -e "$MNT_ROOT" && ! -L "$MNT_ROOT" ]]; then say 'No benchmark resources to clean'; return 0; fi
    step '[cleanup] verify ownership, unmount, detach, remove' cleanup_all
    return 0
  fi
  step '[prepare] dependencies' install_deps
  step '[prepare] environment and capacity' check_environment
  mkdir -m 700 "$WORK"
  printf 'wsl-btrfs-bench-v2:%s\n' "$UID" > "$WORK/owner"
  OWNED=1
  root mkdir -m 755 "$MNT_ROOT"
  printf 'variant\tmountpoint\timage\tloop\tround\next4\t%s\t-\t-\tall\n' "$BASE" > "$WORK/mounts.tsv"
  step '[prepare] environment capture' capture_env
  step '[prepare] cache-drop availability' cache_reset
  step '[prepare] Git data generation' prepare_git_repo
  mkdir "$BASE"
  local v index=0 round dir rounds order
  printf 'round\tposition\tvariant\tworkloads\n' > "$RESULTS/order.tsv"
  printf 'variant\ttest\tdirect\tcache_before\tdrop_caches_enabled\n' > "$RESULTS/cache.tsv"
  rounds=$SMALL_RUNS
  (( GIT_RUNS <= rounds )) || rounds=$GIT_RUNS
  for ((round=1; round<=rounds; round++)); do
    # Baseline first, middle, last; reverse/rotate all image-backed conditions.
    local others=("${VARIANTS[@]:1}") arranged=() i offset slot
    offset=$(( (round-1)*3 % ${#others[@]} ))
    for ((i=0; i<${#others[@]}; i++)); do
      if (( round%2 == 0 )); then slot=$(( (${#others[@]}-1-i+offset)%${#others[@]} ));
      else slot=$(( (i+offset)%${#others[@]} )); fi
      arranged+=("${others[$slot]}")
    done
    case $(( (round-1)%3 )) in
      0) arranged=(ext4 "${arranged[@]}") ;;
      1) slot=$((COUNT/2)); arranged=("${arranged[@]:0:slot}" ext4 "${arranged[@]:slot}") ;;
      2) arranged+=(ext4) ;;
    esac
    order="${arranged[*]}"
    index=0
    for v in $order; do
      index=$((index+1))
      dir="$MNT_ROOT/$v"; [[ "$v" != ext4 ]] || dir="$BASE"
      printf '%s\t%s\t%s\tfio_round1;small<=%s;git<=%s\n' "$round" "$index" "$v" "$SMALL_RUNS" "$GIT_RUNS" >> "$RESULTS/order.tsv"
      if [[ "$v" != ext4 ]]; then step "[$index/$COUNT $v] image / loop / mkfs / mount round $round" setup_variant "$v" "$round"; fi
      step "[$index/$COUNT $v] verify mount round $round" assert_mount "$v"
      if (( round == 1 )); then run_fio_suite "$v" "$dir"; fi
      if (( round <= SMALL_RUNS )); then step "[$index/$COUNT $v] small files $round/$SMALL_RUNS" smallfiles_one "$v" "$dir" "$round"; fi
      if (( round <= GIT_RUNS )); then step "[$index/$COUNT $v] Git $round/$GIT_RUNS" git_one "$v" "$dir" "$round"; fi
      record_storage "$v" "round_$round"
      if [[ "$v" != ext4 ]]; then
        if [[ "$KEEP_MOUNTS" == 1 && "$round" == "$rounds" ]]; then mkdir "$dir/investigate";
        else step "[$index/$COUNT $v] cleanup round $round" cleanup_resources "$v"; fi
      fi
    done
  done
  mkdir "$BASE/investigate"
  step '[summary] validate and write results' summary
}
main "$@"

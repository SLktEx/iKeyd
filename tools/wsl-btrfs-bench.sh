#!/usr/bin/env bash
set -Eeuo pipefail

# WSL ext4 vs loop-backed Btrfs benchmark matrix.
#
# Variants:
#   ext4 baseline
#   sparse: none / LZO / ZSTD:3
#   fixed-preallocated: none / LZO / ZSTD:3
#
# KEEP_MOUNTS=1 leaves all six Btrfs filesystems mounted after the benchmark so
# they can be inspected manually. Run this script with --cleanup to remove them.

RUNTIME="${RUNTIME:-15}"
IMAGE_GIB="${IMAGE_GIB:-16}"
FIO_SIZE="${FIO_SIZE:-1G}"
SMALL_FILES="${SMALL_FILES:-300000}"
SMALL_RUNS="${SMALL_RUNS:-3}"
GIT_FILES="${GIT_FILES:-15000}"
GIT_RUNS="${GIT_RUNS:-3}"
COMPRESS_PCT="${COMPRESS_PCT:-60}"
DROP_CACHES="${DROP_CACHES:-1}"
KEEP_MOUNTS="${KEEP_MOUNTS:-0}"

STAMP="$(date +%Y%m%d-%H%M%S)"
WORK="/var/tmp/wsl-btrfs-bench-${UID}"
BASE="${WORK}/ext4"
MNT_ROOT="/mnt/wsl-btrfs-bench-${UID}"
RESULTS="${HOME}/wsl-btrfs-bench-results-${STAMP}"
SRC="/dev/shm/wsl-btrfs-bench-src-${UID}"

VARIANTS=(
  ext4
  sparse-none
  sparse-lzo
  sparse-zstd
  fixed-none
  fixed-lzo
  fixed-zstd
)

ACTIVE_MNT=""
ACTIVE_LOOP=""
ACTIVE_IMG=""
VARIANT_DIR=""

say() { printf '\n==> %s\n' "$*"; }
warn() { printf '\nWARNING: %s\n' "$*" >&2; }
die() { printf '\nERROR: %s\n' "$*" >&2; exit 1; }

cleanup_active() {
  set +e
  sync
  if [[ -n "${ACTIVE_MNT:-}" ]] && mountpoint -q "$ACTIVE_MNT" 2>/dev/null; then
    sudo umount "$ACTIVE_MNT"
  fi
  if [[ -n "${ACTIVE_LOOP:-}" ]]; then
    sudo losetup -d "$ACTIVE_LOOP" 2>/dev/null || true
  fi
  [[ -n "${ACTIVE_MNT:-}" ]] && sudo rmdir "$ACTIVE_MNT" 2>/dev/null || true
  [[ -n "${ACTIVE_IMG:-}" ]] && rm -f "$ACTIVE_IMG" 2>/dev/null || true
  ACTIVE_MNT=""
  ACTIVE_LOOP=""
  ACTIVE_IMG=""
  set -e
}

cleanup_all() {
  set +e
  sync

  if [[ -d "$MNT_ROOT" ]]; then
    while IFS= read -r target; do
      [[ -n "$target" ]] && sudo umount "$target" 2>/dev/null || true
    done < <(findmnt -rn -o TARGET | awk -v root="$MNT_ROOT/" 'index($0,root)==1' | sort -r)
  fi

  if [[ -d "$WORK" ]]; then
    local img loop
    while IFS= read -r img; do
      [[ -n "$img" ]] || continue
      while IFS= read -r loop; do
        [[ -n "$loop" ]] && sudo losetup -d "$loop" 2>/dev/null || true
      done < <(sudo losetup -j "$img" 2>/dev/null | cut -d: -f1)
    done < <(find "$WORK" -maxdepth 1 -type f -name '*.img' -print 2>/dev/null)
  fi

  rm -rf "$SRC" "$WORK" 2>/dev/null || true
  sudo rm -rf "$MNT_ROOT" 2>/dev/null || true
  set -e
}

on_exit() {
  local rc=$?
  set +e
  rm -rf "$SRC" 2>/dev/null || true
  if [[ "$KEEP_MOUNTS" == "1" ]]; then
    if [[ -d "$WORK" ]]; then
      printf '\nPreserved benchmark filesystems for investigation.\n'
      printf 'Mount root: %s\n' "$MNT_ROOT"
      printf 'Backing files: %s/*.img\n' "$WORK"
      printf 'Cleanup later with: %s --cleanup\n' "$0"
    fi
  else
    cleanup_active
    rm -rf "$WORK" 2>/dev/null || true
    sudo rmdir "$MNT_ROOT" 2>/dev/null || true
  fi
  exit "$rc"
}
trap on_exit EXIT INT TERM

install_deps() {
  local pkgs=()
  command -v fio >/dev/null 2>&1 || pkgs+=(fio)
  command -v mkfs.btrfs >/dev/null 2>&1 || pkgs+=(btrfs-progs)
  command -v git >/dev/null 2>&1 || pkgs+=(git)
  command -v python3 >/dev/null 2>&1 || pkgs+=(python3)
  command -v losetup >/dev/null 2>&1 || pkgs+=(util-linux)
  command -v fallocate >/dev/null 2>&1 || pkgs+=(util-linux)
  [[ -x /usr/bin/time ]] || pkgs+=(time)
  if ((${#pkgs[@]})); then
    say "Installing benchmark dependencies: ${pkgs[*]}"
    sudo apt-get update
    sudo apt-get install -y "${pkgs[@]}"
  fi
}

drop_caches() {
  sync
  if [[ "$DROP_CACHES" == "1" ]]; then
    echo 3 | sudo tee /proc/sys/vm/drop_caches >/dev/null
  fi
}

check_environment() {
  [[ "$(uname -s)" == Linux ]] || die "Run this inside WSL/Linux."
  grep -qi microsoft /proc/version || warn "Kernel does not identify itself as WSL; continuing anyway."

  local fstype free_gib need_gib
  fstype="$(findmnt -n -o FSTYPE -T /var/tmp)"
  [[ "$fstype" == ext4 ]] || die "/var/tmp is '$fstype', not ext4. The baseline and Btrfs backing images must live on WSL ext4."

  if ! grep -qw btrfs /proc/filesystems; then
    sudo modprobe btrfs 2>/dev/null || true
  fi
  grep -qw btrfs /proc/filesystems || die "This kernel does not expose Btrfs support."

  [[ ! -e "$WORK" ]] || die "$WORK already exists. Run '$0 --cleanup' before starting another benchmark."

  free_gib="$(df -BG --output=avail /var/tmp | tail -1 | tr -dc '0-9')"
  if [[ "$KEEP_MOUNTS" == "1" ]]; then
    need_gib=$(( IMAGE_GIB * 3 + 12 ))
  else
    need_gib=$(( IMAGE_GIB + 4 ))
  fi
  if [[ -n "$free_gib" ]] && (( free_gib < need_gib )); then
    die "Only ~${free_gib} GiB free on ext4; this mode needs roughly ${need_gib} GiB free. Lower IMAGE_GIB if needed."
  fi
}

capture_env() {
  {
    echo "timestamp=$(date -Is)"
    echo "uname=$(uname -a)"
    echo "kernel=$(uname -r)"
    echo "root=$(findmnt -n -o SOURCE,FSTYPE,OPTIONS /)"
    echo "var_tmp=$(findmnt -n -o SOURCE,FSTYPE,OPTIONS -T /var/tmp)"
    echo "fio=$(fio --version 2>/dev/null || true)"
    echo "btrfs=$(btrfs --version 2>/dev/null || true)"
    echo "git=$(git --version 2>/dev/null || true)"
    echo "image_gib=$IMAGE_GIB"
    echo "fio_size=$FIO_SIZE"
    echo "runtime=$RUNTIME"
    echo "small_files=$SMALL_FILES"
    echo "small_runs=$SMALL_RUNS"
    echo "git_files=$GIT_FILES"
    echo "git_runs=$GIT_RUNS"
    echo "compress_pct=$COMPRESS_PCT"
    echo "drop_caches=$DROP_CACHES"
    echo "keep_mounts=$KEEP_MOUNTS"
    printf 'variants=%s\n' "${VARIANTS[*]}"
    command -v wsl.exe >/dev/null 2>&1 && {
      echo '--- wsl.exe --version ---'
      wsl.exe --version 2>/dev/null | tr -d '\r' || true
    }
  } > "$RESULTS/environment.txt"
}

prepare_git_repo() {
  say "Preparing synthetic Git repository in tmpfs (${GIT_FILES} files)"
  rm -rf "$SRC"
  mkdir -p "$SRC"
  git -C "$SRC" init -q
  git -C "$SRC" config user.email bench@example.invalid
  git -C "$SRC" config user.name Bench
  python3 - "$SRC" "$GIT_FILES" <<'PY'
import os, sys
root = sys.argv[1]
n = int(sys.argv[2])
body = ("class Example { static final String VALUE = \"0123456789abcdef\"; }\n" * 16)
for i in range(n):
    d = os.path.join(root, f'src/pkg{i//250:04d}')
    if i % 250 == 0:
        os.makedirs(d, exist_ok=True)
    with open(os.path.join(d, f'file{i:07d}.txt'), 'w') as f:
        f.write(f'{i}\n{body}')
PY
  git -C "$SRC" add -A
  git -C "$SRC" commit -q -m bench
  git -C "$SRC" gc --aggressive --prune=now >/dev/null 2>&1 || true
}

variant_parts() {
  local variant="$1"
  if [[ "$variant" == ext4 ]]; then
    printf 'ext4 none\n'
  else
    printf '%s %s\n' "${variant%%-*}" "${variant#*-}"
  fi
}

setup_variant() {
  local variant="$1" provision compression opts setup_start setup_end
  read -r provision compression < <(variant_parts "$variant")

  if [[ "$variant" == ext4 ]]; then
    mkdir -p "$BASE"
    VARIANT_DIR="$BASE"
    printf 'ext4\t%s\t-\t-\n' "$BASE" >> "$RESULTS/mounts.tsv"
    return
  fi

  ACTIVE_IMG="${WORK}/${variant}.img"
  ACTIVE_MNT="${MNT_ROOT}/${variant}"
  sudo mkdir -p "$ACTIVE_MNT"

  setup_start="$(date +%s%N)"
  case "$provision" in
    sparse) truncate -s "${IMAGE_GIB}G" "$ACTIVE_IMG" ;;
    fixed)  fallocate -l "${IMAGE_GIB}G" "$ACTIVE_IMG" ;;
    *) die "Unknown provisioning mode: $provision" ;;
  esac

  ACTIVE_LOOP="$(sudo losetup --find --show "$ACTIVE_IMG")"
  sudo mkfs.btrfs -q -f -L "wsl-bench-${variant}" "$ACTIVE_LOOP"

  opts="noatime"
  case "$compression" in
    none) ;;
    lzo)  opts+=",compress=lzo" ;;
    zstd) opts+=",compress=zstd:3" ;;
    *) die "Unknown compression mode: $compression" ;;
  esac

  sudo mount -t btrfs -o "$opts" "$ACTIVE_LOOP" "$ACTIVE_MNT"
  sudo chown "$USER":"$(id -gn)" "$ACTIVE_MNT"
  setup_end="$(date +%s%N)"

  {
    echo -e "variant\t$variant"
    echo -e "provision\t$provision"
    echo -e "compression\t$compression"
    echo -e "logical_bytes\t$(stat -c %s "$ACTIVE_IMG")"
    echo -e "outer_physical_bytes_after_setup\t$(du -B1 "$ACTIVE_IMG" | awk '{print $1}')"
    echo -e "setup_seconds\t$(python3 - <<PY
print((${setup_end}-${setup_start})/1e9)
PY
)"
    echo -e "mount\t$(findmnt -n -o SOURCE,FSTYPE,OPTIONS -T "$ACTIVE_MNT")"
  } > "$RESULTS/storage_${variant}.tsv"

  printf '%s\t%s\t%s\t%s\n' "$variant" "$ACTIVE_MNT" "$ACTIVE_IMG" "$ACTIVE_LOOP" >> "$RESULTS/mounts.tsv"
  VARIANT_DIR="$ACTIVE_MNT"
}

record_variant_storage_after() {
  local variant="$1"
  [[ "$variant" == ext4 ]] && return
  {
    echo -e "outer_physical_bytes_after_workloads\t$(du -B1 "$ACTIVE_IMG" | awk '{print $1}')"
    echo -e "inner_used_bytes\t$(sudo btrfs filesystem usage -b "$ACTIVE_MNT" 2>/dev/null | awk '/Used:/ {print $2; exit}')"
  } >> "$RESULTS/storage_${variant}.tsv"
}

fio_one() {
  local variant="$1" dir="$2" test="$3" rw="$4" bs="$5" direct="$6" do_fsync="$7"
  local out="$RESULTS/fio_${variant}_${test}.json"
  local file="$dir/fio.bin"

  drop_caches
  local args=(
    --name="$test"
    --filename="$file"
    --size="$FIO_SIZE"
    --rw="$rw"
    --bs="$bs"
    --ioengine=sync
    --iodepth=1
    --numjobs=1
    --direct="$direct"
    --time_based=1
    --runtime="$RUNTIME"
    --ramp_time=2
    --randrepeat=0
    --group_reporting=1
    --output-format=json
    --output="$out"
  )
  if [[ "$rw" == *write* ]]; then
    args+=(--buffer_compress_percentage="$COMPRESS_PCT" --buffer_compress_chunk=4k --end_fsync=1)
  fi
  if [[ -n "$do_fsync" ]]; then
    args+=(--fsync="$do_fsync")
  fi
  fio "${args[@]}"
  sync
}

run_fio_suite() {
  local variant="$1" dir="$2"
  say "[$variant] fio suite"
  rm -f "$dir/fio.bin"
  fio_one "$variant" "$dir" seq-write-cold write     1M 0 ""
  fio_one "$variant" "$dir" seq-write-warm write     1M 0 ""
  fio_one "$variant" "$dir" seq-read       read      1M 1 ""
  fio_one "$variant" "$dir" rand-read      randread  4k 1 ""
  fio_one "$variant" "$dir" rand-write     randwrite 4k 0 ""
  fio_one "$variant" "$dir" fsync-write    randwrite 4k 0 1
}

smallfiles_one() {
  local variant="$1" dir="$2" run="$3"
  local target="$dir/smallfiles-$run"
  rm -rf "$target"
  drop_caches
  /usr/bin/time -f '%e' -o "$RESULTS/smallfiles_${variant}_${run}.time" \
    python3 - "$target" "$SMALL_FILES" <<'PY'
import os, sys, shutil
root = sys.argv[1]
n = int(sys.argv[2])
os.makedirs(root, exist_ok=True)
payload = (("int value = 123456789; // benchmark source-like payload\n" * 20).encode())[:1024]
for i in range(n):
    d = os.path.join(root, f'd{i//1000:04d}')
    if i % 1000 == 0:
        os.makedirs(d, exist_ok=True)
    p = os.path.join(d, f'f{i:07d}.txt')
    with open(p, 'wb', buffering=0) as f:
        f.write(payload)
for i in range(n):
    os.stat(os.path.join(root, f'd{i//1000:04d}', f'f{i:07d}.txt'))
for i in range(0, n, 2):
    d = os.path.join(root, f'd{i//1000:04d}')
    os.rename(os.path.join(d, f'f{i:07d}.txt'), os.path.join(d, f'r{i:07d}.txt'))
os.sync()
shutil.rmtree(root)
os.sync()
PY
}

run_smallfiles_suite() {
  local variant="$1" dir="$2" i
  say "[$variant] small-file metadata workload (${SMALL_FILES} files x ${SMALL_RUNS} runs)"
  for i in $(seq 1 "$SMALL_RUNS"); do
    smallfiles_one "$variant" "$dir" "$i"
  done
}

git_one() {
  local variant="$1" dir="$2" run="$3"
  local dest="$dir/git-clone-$run"
  rm -rf "$dest"
  drop_caches
  /usr/bin/time -f '%e' -o "$RESULTS/git_${variant}_${run}.time" \
    git clone -q --no-local "file://$SRC" "$dest"
  sync
  rm -rf "$dest"
}

run_git_suite() {
  local variant="$1" dir="$2" i
  say "[$variant] Git clone/unpack workload (${GIT_RUNS} runs)"
  for i in $(seq 1 "$GIT_RUNS"); do
    git_one "$variant" "$dir" "$i"
  done
}

run_variant() {
  local variant="$1" dir
  say "Variant: $variant"
  setup_variant "$variant"
  dir="$VARIANT_DIR"
  run_fio_suite "$variant" "$dir"
  run_smallfiles_suite "$variant" "$dir"
  run_git_suite "$variant" "$dir"
  record_variant_storage_after "$variant"

  if [[ "$KEEP_MOUNTS" == "1" ]]; then
    mkdir -p "$dir/investigate"
    ACTIVE_MNT=""
    ACTIVE_LOOP=""
    ACTIVE_IMG=""
  elif [[ "$variant" != ext4 ]]; then
    cleanup_active
  fi
}

summary() {
  python3 - "$RESULTS" <<'PY'
import glob, json, math, os, statistics, sys
r = sys.argv[1]
variants = ['ext4','sparse-none','sparse-lzo','sparse-zstd','fixed-none','fixed-lzo','fixed-zstd']

def fio(v, test, kind, field):
    with open(os.path.join(r, f'fio_{v}_{test}.json')) as f:
        j = json.load(f)
    return float(j['jobs'][0][kind].get(field, 0.0))

def times(prefix, v):
    vals=[]
    for p in sorted(glob.glob(os.path.join(r, f'{prefix}_{v}_*.time'))):
        with open(p) as f:
            vals.append(float(f.read().strip()))
    return statistics.median(vals), vals

metrics=[]
for test, kind in [('seq-write-cold','write'),('seq-write-warm','write'),('seq-read','read')]:
    metrics.append((test,'MB/s',True,{v:fio(v,test,kind,'bw_bytes')/1e6 for v in variants}))
for test, kind in [('rand-read','read'),('rand-write','write'),('fsync-write','write')]:
    metrics.append((test,'IOPS',True,{v:fio(v,test,kind,'iops') for v in variants}))
metrics.append(('small-files','sec',False,{v:times('smallfiles',v)[0] for v in variants}))
metrics.append(('git-clone','sec',False,{v:times('git',v)[0] for v in variants}))

scores={v:[] for v in variants}
for _,_,higher,vals in metrics:
    e=vals['ext4']
    for v in variants:
        x=vals[v]
        ratio=(x/e) if higher else (e/x)
        scores[v].append(max(ratio,1e-12))
overall={v:math.exp(sum(math.log(x) for x in rs)/len(rs))*100 for v,rs in scores.items()}

lines=['WSL ext4 vs loop-backed Btrfs benchmark matrix','', 'Overall relative performance (ext4 = 100):']
for v in variants:
    lines.append(f'  {v:<14} {overall[v]:7.1f}')
lines.append('')
hdr=f"{'test':<18} {'unit':>7}" + ''.join(f" {v:>14}" for v in variants)
lines += [hdr, '-'*len(hdr)]
for name,unit,_,vals in metrics:
    lines.append(f"{name:<18} {unit:>7}" + ''.join(f" {vals[v]:14.2f}" for v in variants))
lines += ['', 'Sparse vs fixed overall performance, same compression (positive = sparse faster):']
for comp in ('none','lzo','zstd'):
    s=overall[f'sparse-{comp}']; f=overall[f'fixed-{comp}']
    lines.append(f'  {comp:<5} {(s/f-1)*100:+7.2f}%')
lines += ['', 'Small-file run times (all runs, seconds):']
for v in variants:
    med, vals = times('smallfiles',v)
    lines.append(f"  {v:<14} runs={','.join(f'{x:.2f}' for x in vals)} median={med:.2f}")
lines += ['', 'Backing-image storage:']
for v in variants:
    if v == 'ext4':
        continue
    kv={}
    with open(os.path.join(r,f'storage_{v}.tsv')) as f:
        for line in f:
            k,val=line.rstrip('\n').split('\t',1); kv[k]=val
    logical=int(kv.get('logical_bytes','0'))
    setup=int(kv.get('outer_physical_bytes_after_setup','0'))
    after=int(kv.get('outer_physical_bytes_after_workloads','0'))
    lines.append(f"  {v:<14} logical={logical/2**30:.2f}GiB setup={setup/2**30:.2f}GiB after={after/2**30:.2f}GiB")
lines += ['', 'Notes:',
          '  - Btrfs compression modes are none, LZO, and ZSTD level 3.',
          '  - sparse uses truncate; fixed uses fallocate on the outer ext4 filesystem.',
          '  - fio write buffers are moderately compressible so compression is exercised.',
          '  - small-files and Git are application-style workloads; fio is synthetic.']
text='\n'.join(lines)+'\n'
print(text)
with open(os.path.join(r,'summary.txt'),'w') as f: f.write(text)
PY
}

print_investigation_paths() {
  [[ "$KEEP_MOUNTS" == "1" ]] || return 0
  say "Preserved investigation filesystems"
  printf '%-14s %s\n' "ext4" "$BASE/investigate"
  local variant
  for variant in sparse-none sparse-lzo sparse-zstd fixed-none fixed-lzo fixed-zstd; do
    printf '%-14s %s\n' "$variant" "$MNT_ROOT/$variant/investigate"
  done
  printf '\nBacking images: %s/*.img\n' "$WORK"
  printf 'Mount metadata: %s/mounts.tsv\n' "$RESULTS"
  printf 'Cleanup: %s --cleanup\n' "$0"
}

main() {
  if [[ "${1:-}" == "--cleanup" ]]; then
    trap - EXIT INT TERM
    sudo -v
    cleanup_all
    say "Removed preserved benchmark mounts, loop devices, and backing images"
    exit 0
  fi
  [[ $# -eq 0 ]] || die "Usage: $0 [--cleanup]"
  [[ "$KEEP_MOUNTS" == "0" || "$KEEP_MOUNTS" == "1" ]] || die "KEEP_MOUNTS must be 0 or 1"

  say "WSL ext4 vs loop-backed Btrfs benchmark matrix"
  cat <<MSG
Variants:
  ext4
  sparse-none / sparse-lzo / sparse-zstd
  fixed-none  / fixed-lzo  / fixed-zstd

Btrfs image size: ${IMAGE_GIB} GiB each.
Small-file workload: ${SMALL_FILES} files x ${SMALL_RUNS} runs per variant.
KEEP_MOUNTS=${KEEP_MOUNTS}

Fixed images use fallocate and may grow the Windows-side WSL ext4.vhdx. Deleting
those files frees space inside ext4, but the VHDX file itself may not immediately
shrink on Windows.
MSG

  sudo -v
  install_deps
  check_environment
  mkdir -p "$WORK" "$BASE" "$RESULTS"
  sudo mkdir -p "$MNT_ROOT"
  printf 'variant\tmountpoint\timage\tloop\n' > "$RESULTS/mounts.tsv"
  capture_env
  prepare_git_repo

  local variant
  for variant in "${VARIANTS[@]}"; do
    run_variant "$variant"
  done

  mkdir -p "$BASE/investigate"
  say "Results"
  summary
  print_investigation_paths
  echo "Full results: $RESULTS"
}

main "$@"

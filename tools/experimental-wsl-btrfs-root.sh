#!/usr/bin/env bash
set -Eeuo pipefail

# Experimental: make the effective WSL distribution root filesystem Btrfs
# while keeping the WSL-managed ext4.vhdx as a bootstrap/backing store.
#
# Layout after installation:
#   ext4.vhdx (WSL-managed)
#     /var/lib/wsl-btrfs-bootstrap/root.btrfs.img  (sparse file)
#       -> loop device -> Btrfs subvolume @ -> /
#
# WSL still initially boots its ext4 root. Native WSL systemd support launches
# /sbin/init; this installer replaces only the ext4-side /sbin/init with a
# wrapper. The wrapper mounts the Btrfs image, carries WSL's runtime mounts and
# /init into the new root, pivot_root(2)s, then execs the original /sbin/init
# that was copied into Btrfs before the wrapper was installed.
#
# This is intentionally experimental. Export anything you care about first:
#   PowerShell> wsl --export <DistroName> backup.tar
#
# If you are happy to recreate the distro when it breaks, recovery is simple:
#   PowerShell> wsl --unregister <DistroName>
# (That permanently deletes that distro's data.)

BOOTSTRAP_DIR=/var/lib/wsl-btrfs-bootstrap
IMAGE_PATH="$BOOTSTRAP_DIR/root.btrfs.img"
STAGE_PATH="$BOOTSTRAP_DIR/newroot"
WRAPPER_PATH=/usr/local/libexec/wsl-btrfs-root-init
DEFAULT_SIZE=${WSL_BTRFS_ROOT_SIZE:-256G}
MOUNT_OPTS=${WSL_BTRFS_MOUNT_OPTS:-subvol=@,compress=zstd:1,noatime}
LABEL=wsl-btrfs-root

log()  { printf '[wsl-btrfs-root] %s\n' "$*"; }
warn() { printf '[wsl-btrfs-root] WARNING: %s\n' "$*" >&2; }
die()  { printf '[wsl-btrfs-root] ERROR: %s\n' "$*" >&2; exit 1; }

usage() {
  cat <<EOF
Usage:
  sudo $0 install [SIZE]
  sudo $0 resize SIZE
       $0 status

Examples:
  sudo $0 install          # 256G sparse Btrfs image
  sudo $0 install 512G
  sudo $0 resize 768G      # after booting into Btrfs
  $0 status

After 'install', run this from PowerShell and reopen the distro:
  wsl --shutdown
EOF
}

is_wsl2() {
  grep -qiE 'microsoft.*wsl2|microsoft-standard-WSL2' /proc/sys/kernel/osrelease 2>/dev/null \
    || [[ -n "${WSL_INTEROP:-}" && -n "${WSL_DISTRO_NAME:-}" ]]
}

root_fstype() {
  findmnt -n -o FSTYPE / 2>/dev/null || stat -f -c %T /
}

require_root() {
  [[ ${EUID:-$(id -u)} -eq 0 ]] || die 'run this command with sudo/root'
}

install_packages() {
  local missing=0
  for cmd in rsync mkfs.btrfs btrfs losetup pivot_root mount findmnt; do
    command -v "$cmd" >/dev/null 2>&1 || missing=1
  done
  (( missing == 0 )) && return 0

  log 'Installing rsync, btrfs-progs and util-linux...'
  if command -v apt-get >/dev/null 2>&1; then
    apt-get update
    DEBIAN_FRONTEND=noninteractive apt-get install -y rsync btrfs-progs util-linux
  elif command -v dnf >/dev/null 2>&1; then
    dnf install -y rsync btrfs-progs util-linux
  elif command -v zypper >/dev/null 2>&1; then
    zypper --non-interactive install rsync btrfsprogs util-linux
  elif command -v pacman >/dev/null 2>&1; then
    pacman -Sy --noconfirm rsync btrfs-progs util-linux
  else
    die 'install rsync, btrfs-progs and util-linux, then retry'
  fi
}

set_wsl_systemd_true() {
  local file=/etc/wsl.conf tmp
  tmp=$(mktemp)
  [[ -e "$file" ]] || : >"$file"
  [[ -e "$file.pre-wsl-btrfs-root" ]] || cp -a "$file" "$file.pre-wsl-btrfs-root"

  awk '
    BEGIN { inboot=0; seenboot=0; seensystemd=0 }
    /^[[:space:]]*\[/ {
      if (inboot && !seensystemd) print "systemd=true"
      inboot=0
      if ($0 ~ /^[[:space:]]*\[boot\][[:space:]]*$/) {
        inboot=1; seenboot=1; seensystemd=0
      }
      print
      next
    }
    {
      if (inboot && $0 ~ /^[[:space:]]*systemd[[:space:]]*=/) {
        if (!seensystemd) print "systemd=true"
        seensystemd=1
        next
      }
      print
    }
    END {
      if (inboot && !seensystemd) print "systemd=true"
      if (!seenboot) {
        print ""
        print "[boot]"
        print "systemd=true"
      }
    }
  ' "$file" >"$tmp"
  cat "$tmp" >"$file"
  rm -f "$tmp"
}

human_bytes() {
  numfmt --to=iec-i --suffix=B "$1" 2>/dev/null || printf '%s bytes' "$1"
}

write_boot_wrapper() {
  local original_init=$1
  install -d -m 0755 "$(dirname "$WRAPPER_PATH")"

  cat >"$WRAPPER_PATH" <<EOF
#!/usr/bin/env bash
set -Eeuo pipefail

BOOTSTRAP_DIR='$BOOTSTRAP_DIR'
IMAGE_PATH='$IMAGE_PATH'
NEWROOT='$STAGE_PATH'
MOUNT_OPTS='$MOUNT_OPTS'
ORIGINAL_INIT='$original_init'

klog() {
  local msg="wsl-btrfs-root: \$*"
  printf '%s\\n' "\$msg" >/dev/kmsg 2>/dev/null || true
}

fallback() {
  trap - ERR
  set +e
  klog "Btrfs root activation failed; booting the original ext4 root: \$*"
  if mountpoint -q "\$NEWROOT" 2>/dev/null; then
    umount -R "\$NEWROOT" 2>/dev/null || umount -l "\$NEWROOT" 2>/dev/null || true
  fi
  exec "\$ORIGINAL_INIT"
}

trap 'fallback "unexpected error at line \$LINENO"' ERR

bind_runtime_mount() {
  local src=\$1 dst="\$NEWROOT\$1"
  mkdir -p "\$dst"
  mount --rbind "\$src" "\$dst" || return 1
  # Receive future propagation from WSL, but do not propagate unmounts back.
  mount --make-rslave "\$dst" 2>/dev/null || true
}

[[ -f "\$IMAGE_PATH" ]] || fallback "missing \$IMAGE_PATH"

# WSL kernels normally autoload filesystem modules, but make it explicit when
# the distro has modprobe available.
command -v modprobe >/dev/null 2>&1 && modprobe btrfs 2>/dev/null || true

mkdir -p "\$NEWROOT"
if ! mount -t btrfs -o "loop,\$MOUNT_OPTS" "\$IMAGE_PATH" "\$NEWROOT"; then
  fallback 'mount failed'
fi

# WSL creates these before launching /sbin/init. systemd and WSL init must see
# exactly those existing mounts/sockets after the root switch.
for p in /proc /sys /dev /run; do
  bind_runtime_mount "\$p" || fallback "failed to carry \$p"
done

# /mnt/wsl is shared by WSL distributions. WSLg and DrvFs may appear later,
# but carry any mounts that already exist at this point too.
for p in /mnt/wsl /mnt/wslg /mnt/[a-zA-Z]; do
  [[ -e "\$p" ]] || continue
  mountpoint -q "\$p" 2>/dev/null || continue
  bind_runtime_mount "\$p" || fallback "failed to carry \$p"
done

# WSL interop, mount.drvfs, wslpath, wslinfo and Windows .exe launching depend
# on Microsoft's /init remaining reachable after pivot_root.
if [[ -e /init ]]; then
  [[ -e "\$NEWROOT/init" ]] || touch "\$NEWROOT/init"
  mount --bind /init "\$NEWROOT/init" || fallback 'failed to bind /init'
else
  fallback '/init is missing'
fi

mkdir -p "\$NEWROOT/.wsl-bootstrap"
chmod 0700 "\$NEWROOT/.wsl-bootstrap"

# pivot_root refuses shared parent mounts. Do not recurse: /mnt/wsl itself may
# intentionally have WSL-managed propagation semantics.
mount --make-private / 2>/dev/null || true
mount --make-private "\$NEWROOT" 2>/dev/null || true

cd "\$NEWROOT"
if ! pivot_root . .wsl-bootstrap; then
  cd /
  fallback 'pivot_root failed'
fi
cd /

# Keep the ext4 bootstrap visible at /.wsl-bootstrap. The Btrfs image is
# backed by a file there, and visibility also makes inspection/resizing easy.
# The actual / mount is Btrfs.
klog 'Btrfs root active; execing distro init'
exec /sbin/init
EOF

  chmod 0755 "$WRAPPER_PATH"
}

install_root() {
  require_root
  is_wsl2 || die 'this script is intended for WSL2'
  [[ "$(root_fstype)" != btrfs ]] || die 'root is already Btrfs'
  [[ "$(root_fstype)" == ext4 ]] || warn "current root filesystem is $(root_fstype), not ext4"
  [[ ! -e "$IMAGE_PATH" ]] || die "$IMAGE_PATH already exists; refusing to overwrite it"

  local size=${1:-$DEFAULT_SIZE}
  local original_init
  original_init=$(readlink -f /sbin/init 2>/dev/null || true)
  [[ -n "$original_init" && -x "$original_init" ]] || die 'could not resolve an executable /sbin/init'
  [[ $(basename "$original_init") == systemd ]] || warn "/sbin/init resolves to $original_init (expected systemd)"

  install_packages
  set_wsl_systemd_true

  local used avail size_bytes
  used=$(du -sx -B1 --one-file-system / 2>/dev/null | awk '{print $1}')
  avail=$(df -B1 --output=avail / | tail -n1 | tr -d ' ')
  size_bytes=$(numfmt --from=iec "$size" 2>/dev/null) || die "invalid image size: $size"
  log "Current ext4 root data: $(human_bytes "$used")"
  log "Current ext4 free space: $(human_bytes "$avail")"
  log "Btrfs image logical size: $(human_bytes "$size_bytes") (sparse)"
  (( size_bytes > used + 2147483648 )) || die 'image size is too small for the current root data'
  if (( avail < used + 1073741824 )); then
    warn 'migration temporarily needs roughly another copy of the current root; ext4 free space looks tight'
  fi

  install -d -m 0700 "$BOOTSTRAP_DIR"
  mkdir -p "$STAGE_PATH"
  truncate -s "$size" "$IMAGE_PATH"
  chmod 0600 "$IMAGE_PATH"

  local loopdev=''
  cleanup_install() {
    set +e
    mountpoint -q "$STAGE_PATH" 2>/dev/null && umount -R "$STAGE_PATH"
    [[ -n "$loopdev" ]] && losetup -d "$loopdev" 2>/dev/null
  }
  trap cleanup_install EXIT INT TERM

  loopdev=$(losetup --find --show --nooverlap "$IMAGE_PATH")
  log "Formatting $loopdev as Btrfs..."
  mkfs.btrfs -f -L "$LABEL" "$loopdev"

  mount -t btrfs "$loopdev" "$STAGE_PATH"
  btrfs subvolume create "$STAGE_PATH/@"
  umount "$STAGE_PATH"
  mount -t btrfs -o "$MOUNT_OPTS" "$loopdev" "$STAGE_PATH"

  log 'Copying the existing WSL root into Btrfs...'
  rsync -aHAXx --numeric-ids --info=progress2 \
    --exclude='/var/lib/wsl-btrfs-bootstrap/***' \
    --exclude='/proc/***' \
    --exclude='/sys/***' \
    --exclude='/dev/***' \
    --exclude='/run/***' \
    --exclude='/mnt/***' \
    --exclude='/tmp/***' \
    --exclude='/lost+found' \
    / "$STAGE_PATH/"

  mkdir -p "$STAGE_PATH"/{proc,sys,dev,run,mnt,tmp}
  chmod 1777 "$STAGE_PATH/tmp"
  sync

  umount "$STAGE_PATH"
  losetup -d "$loopdev"
  loopdev=''

  # Crucial ordering: the Btrfs copy above still contains the distro's genuine
  # /sbin/init. Only the ext4 bootstrap gets replaced by this wrapper.
  write_boot_wrapper "$original_init"
  rm -f /sbin/init
  ln -s "$WRAPPER_PATH" /sbin/init

  cat >"$BOOTSTRAP_DIR/install-info" <<EOF
installed_at=$(date -Is)
image=$IMAGE_PATH
size=$size
mount_opts=$MOUNT_OPTS
original_init=$original_init
EOF
  sync
  trap - EXIT INT TERM

  log 'Installed.'
  log 'Now leave WSL and run:  wsl --shutdown'
  log 'Then reopen this distro and run this script with: status'
  warn 'This is experimental. If the distro becomes unusable, recreate/unregister it from Windows.'
}

backing_image_path() {
  if [[ "$(root_fstype)" == btrfs && -f "/.wsl-bootstrap$IMAGE_PATH" ]]; then
    printf '%s\n' "/.wsl-bootstrap$IMAGE_PATH"
  else
    printf '%s\n' "$IMAGE_PATH"
  fi
}

resize_root() {
  require_root
  local size=${1:-}
  [[ -n "$size" ]] || die 'resize requires a size, e.g. 512G'
  [[ "$(root_fstype)" == btrfs ]] || die 'resize is intended to be run after Btrfs is the active root'

  local image loopdev
  image=$(backing_image_path)
  [[ -f "$image" ]] || die "backing image not found: $image"
  truncate -s "$size" "$image"

  loopdev=$(findmnt -n -o SOURCE / | sed 's/\[.*$//')
  [[ "$loopdev" == /dev/loop* ]] || die "root source is not a loop device: $loopdev"
  losetup -c "$loopdev"
  btrfs filesystem resize max /
  log "Resized Btrfs root backing image to $size"
}

status_root() {
  is_wsl2 || warn 'WSL2 was not positively detected'
  local fs src
  fs=$(root_fstype)
  src=$(findmnt -n -o SOURCE / 2>/dev/null || true)
  printf 'root filesystem : %s\n' "$fs"
  printf 'root source     : %s\n' "${src:-unknown}"
  printf '/init           : %s\n' "$([[ -x /init ]] && echo OK || echo MISSING)"
  printf '/run/WSL        : %s\n' "$([[ -d /run/WSL ]] && echo OK || echo MISSING)"
  printf '/mnt/wsl        : %s\n' "$(mountpoint -q /mnt/wsl 2>/dev/null && echo mounted || echo not-mounted)"
  printf 'systemd         : %s\n' "$(systemctl is-system-running 2>/dev/null || echo unavailable)"
  printf 'Windows interop : %s\n' "$(command -v powershell.exe >/dev/null 2>&1 && echo OK || echo not-found)"
  printf 'code            : %s\n' "$(command -v code >/dev/null 2>&1 && echo found || echo not-found)"
  if [[ "$fs" == btrfs ]]; then
    printf 'bootstrap ext4  : %s\n' "$([[ -d /.wsl-bootstrap ]] && echo '/.wsl-bootstrap' || echo hidden/missing)"
    btrfs filesystem usage / 2>/dev/null | sed 's/^/  /' || true
  fi
}

cmd=${1:-}
case "$cmd" in
  install)
    shift
    install_root "${1:-$DEFAULT_SIZE}"
    ;;
  resize)
    shift
    resize_root "${1:-}"
    ;;
  status)
    status_root
    ;;
  -h|--help|help|'')
    usage
    ;;
  *)
    usage >&2
    exit 2
    ;;
esac

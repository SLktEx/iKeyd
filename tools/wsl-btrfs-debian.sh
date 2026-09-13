#!/usr/bin/env bash
set -Eeuo pipefail

# Experimental Debian-on-WSL2 root-on-Btrfs.
# Base: `wsl --install -d Debian`.
# WSL keeps ext4.vhdx as a tiny bootstrap; the real Debian root lives in a
# sparse loop-backed Btrfs image inside that VHDX.

SELF=/usr/local/sbin/wsl-btrfs-root
BOOT=/var/lib/wsl-btrfs-bootstrap
IMG=$BOOT/root.btrfs.img
NEW=$BOOT/newroot
BB=$BOOT/busybox
BOOT_INIT=$BOOT/init
SIZE=${WSL_BTRFS_ROOT_SIZE:-2T}
OPTS=${WSL_BTRFS_MOUNT_OPTS:-subvol=@,compress=zstd:1,noatime}

log(){ printf '[wsl-btrfs-root] %s\n' "$*"; }
die(){ printf '[wsl-btrfs-root] ERROR: %s\n' "$*" >&2; exit 1; }
rootfs(){ findmnt -n -o FSTYPE /; }
root(){ [[ ${EUID:-$(id -u)} -eq 0 ]] || die 'use sudo'; }
debian(){ . /etc/os-release; [[ ${ID:-} == debian ]] || die 'Debian WSL is required'; }
wsl2(){ grep -qi microsoft /proc/sys/kernel/osrelease || die 'WSL2 is required'; }

usage(){ cat <<EOF2
Usage:
  sudo $0 install [SIZE]
  sudo $0 update
  sudo $0 sync-bootstrap
  sudo $0 compact
       $0 status

Recommended base (PowerShell):
  wsl --install -d Debian

After install:
  wsl --shutdown
EOF2
}

systemd_on(){
  local f=/etc/wsl.conf t; t=$(mktemp); [[ -e $f ]] || : >$f
  awk 'BEGIN{b=0;s=0;B=0}
    /^\[/ {if(b&&!s)print "systemd=true"; b=0; if($0=="[boot]"){b=1;B=1;s=0} print; next}
    {if(b&&$0~/^[[:space:]]*systemd[[:space:]]*=/){if(!s)print "systemd=true";s=1;next} print}
    END{if(b&&!s)print "systemd=true";if(!B)print "\n[boot]\nsystemd=true"}' "$f" >$t
  cat "$t" >$f; rm -f "$t"
}

boot_script(){ cat <<EOF2
#!$BB sh
set -eu
B='$BB'; I='$IMG'; N='$NEW'; O='$OPTS'
b(){ "\$B" "\$@"; }
fail(){ echo "wsl-btrfs-root: \$*" >&2; exec "\$B" sh; }
mounted(){ b awk -v p="\$1" '\$5==p{f=1} END{exit !f}' /proc/self/mountinfo; }
bind(){ b mkdir -p "\$N\$1"; b mount -o rbind "\$1" "\$N\$1"; b mount -o rslave "\$N\$1" 2>/dev/null || true; }
[ -x "\$B" ] || fail 'busybox missing'; [ -f "\$I" ] || fail 'image missing'
if [ -d /run/booted-system/kernel-modules/lib/modules ]; then
  b mkdir -p /usr/lib; b rm -rf /usr/lib/modules 2>/dev/null || true
  b ln -s /run/booted-system/kernel-modules/lib/modules /usr/lib/modules 2>/dev/null || true
fi
b modprobe btrfs 2>/dev/null || true
b grep -qw btrfs /proc/filesystems || fail 'Btrfs unavailable'
L="\$(b losetup -f)" || fail 'no loop device'; b losetup "\$L" "\$I" || fail 'losetup failed'
b mkdir -p "\$N"; b mount -t btrfs -o "\$O" "\$L" "\$N" || fail 'Btrfs mount failed'
for p in /proc /sys /dev /run /mnt; do [ -e "\$p" ] && bind "\$p"; done
for p in /usr/lib/wsl; do [ -e "\$p" ] && mounted "\$p" && bind "\$p" || true; done
[ -e /init ] || fail '/init missing'; [ -e "\$N/init" ] || b touch "\$N/init"; b mount -o bind /init "\$N/init"
b mkdir -p "\$N/.wsl-bootstrap"; b mount -o rprivate / 2>/dev/null || true
cd "\$N"; b pivot_root . .wsl-bootstrap || fail 'pivot_root failed'; cd /
exec /sbin/init "\$@"
EOF2
}

bootstrap(){
  local r=$1 src; src=$(command -v busybox); [[ -x $src ]] || die 'busybox-static missing'
  mkdir -p "$r$BOOT" "$r/usr/bin" "$r/usr/sbin" "$r/usr/lib/wsl" "$r"/{proc,sys,dev,run,mnt,tmp,home,root}
  install -m755 "$src" "$r$BB"; boot_script >"$r$BOOT_INIT"; chmod 755 "$r$BOOT_INIT"; chmod 1777 "$r/tmp"
  ln -snf usr/bin "$r/bin"; ln -snf usr/sbin "$r/sbin"; [[ -e $r/lib || -L $r/lib ]] || ln -s usr/lib "$r/lib"
  ln -snf "$BOOT_INIT" "$r/usr/sbin/init"
  for x in sh mount umount mkdir ln rm cp mv cat grep awk sed true false; do ln -snf "$BB" "$r/usr/bin/$x"; done
  printf '#!%s sh\nexit 0\n' "$BB" >"$r/usr/sbin/ldconfig"; chmod 755 "$r/usr/sbin/ldconfig"
}

sync_etc(){ local r=$1; mkdir -p "$r/etc"; rsync -aHAX --delete /etc/ "$r/etc/"; }

units(){
  local r=$1 d=$r/etc/systemd/system; mkdir -p "$d/multi-user.target.wants" "$d/timers.target.wants"
  cat >"$d/wsl-btrfs-firstboot.service" <<'EOF2'
[Unit]
Description=Compact WSL ext4 bootstrap after first Btrfs boot
ConditionPathExists=/.wsl-bootstrap/var/lib/wsl-btrfs-bootstrap/needs-compact
[Service]
Type=oneshot
ExecStart=/usr/local/sbin/wsl-btrfs-root compact
[Install]
WantedBy=multi-user.target
EOF2
  cat >"$d/wsl-btrfs-update.service" <<'EOF2'
[Unit]
Description=Update Debian WSL Btrfs root
After=network-online.target
[Service]
Type=oneshot
ExecStart=/usr/local/sbin/wsl-btrfs-root update
EOF2
  cat >"$d/wsl-btrfs-update.timer" <<'EOF2'
[Unit]
Description=Daily Debian WSL update
[Timer]
OnBootSec=15min
OnUnitActiveSec=1d
RandomizedDelaySec=30min
Persistent=true
[Install]
WantedBy=timers.target
EOF2
  cat >"$d/wsl-btrfs-ldconfig.service" <<'EOF2'
[Unit]
Description=Refresh linker cache after WSL root pivot
After=local-fs.target
[Service]
Type=oneshot
ExecStart=/sbin/ldconfig
[Install]
WantedBy=multi-user.target
EOF2
  ln -snf ../wsl-btrfs-firstboot.service "$d/multi-user.target.wants/wsl-btrfs-firstboot.service"
  ln -snf ../wsl-btrfs-ldconfig.service "$d/multi-user.target.wants/wsl-btrfs-ldconfig.service"
  ln -snf ../wsl-btrfs-update.timer "$d/timers.target.wants/wsl-btrfs-update.timer"
}

sync_bootstrap(){
  root; [[ $(rootfs) == btrfs ]] || die 'boot into Btrfs first'; local r=/.wsl-bootstrap
  [[ -f $r$IMG ]] || die 'bootstrap image missing'; bootstrap "$r"; sync_etc "$r"; sync
}

compact(){
  root; [[ $(rootfs) == btrfs ]] || die 'boot into Btrfs first'; local r=/.wsl-bootstrap
  sync_bootstrap
  for p in bin boot home lib lib64 media opt root sbin srv usr tmp; do rm -rf --one-file-system "$r/$p" 2>/dev/null || true; done
  find "$r/var" -mindepth 1 -maxdepth 1 ! -name lib -exec rm -rf --one-file-system {} + 2>/dev/null || true
  mkdir -p "$r/var/lib"; find "$r/var/lib" -mindepth 1 -maxdepth 1 ! -name wsl-btrfs-bootstrap -exec rm -rf --one-file-system {} + 2>/dev/null || true
  bootstrap "$r"; sync_etc "$r"; rm -f "$r$BOOT/needs-compact"; touch "$r$BOOT/compacted"; sync
  log 'old Debian copy removed; ext4 is now only the WSL bootstrap + Btrfs image'
}

update(){
  root; debian; [[ $(rootfs) == btrfs ]] || die 'boot into Btrfs first'
  exec 9>/run/wsl-btrfs-update.lock; flock -n 9 || exit 0
  apt-get update; DEBIAN_FRONTEND=noninteractive apt-get -y dist-upgrade
  DEBIAN_FRONTEND=noninteractive apt-get install -y busybox-static btrfs-progs util-linux rsync
  sync_bootstrap; log 'Debian and bootstrap updated'
}

install_root(){
  root; wsl2; debian; [[ $(rootfs) == ext4 ]] || die 'normal ext4 WSL root required'
  [[ ! -e $IMG ]] || die 'already installed'; local size=${1:-$SIZE}
  apt-get update; DEBIAN_FRONTEND=noninteractive apt-get -y dist-upgrade
  DEBIAN_FRONTEND=noninteractive apt-get install -y busybox-static btrfs-progs util-linux rsync
  systemd_on; install -D -m755 "$(readlink -f "$0")" "$SELF"
  mkdir -p "$BOOT" "$NEW"; truncate -s "$size" "$IMG"; chmod 600 "$IMG"
  local l; l=$(losetup --find --show --nooverlap "$IMG"); trap 'umount "$NEW" 2>/dev/null||true; losetup -d "$l" 2>/dev/null||true' EXIT
  mkfs.btrfs -f -L wsl-btrfs-root "$l"; mount -t btrfs "$l" "$NEW"; btrfs subvolume create "$NEW/@"; umount "$NEW"
  mount -t btrfs -o "$OPTS" "$l" "$NEW"
  rsync -aHAXx --numeric-ids --exclude="$BOOT/***" --exclude='/proc/***' --exclude='/sys/***' --exclude='/dev/***' --exclude='/run/***' --exclude='/mnt/***' --exclude='/tmp/***' / "$NEW/"
  mkdir -p "$NEW"/{proc,sys,dev,run,mnt,tmp}; chmod 1777 "$NEW/tmp"; units "$NEW"; touch "$BOOT/needs-compact"
  bootstrap /; sync; umount "$NEW"; losetup -d "$l"; trap - EXIT
  log 'installed; run `wsl --shutdown` from PowerShell and reopen Debian'
}

status(){
  . /etc/os-release 2>/dev/null || true
  printf 'Debian: %s (%s)\n' "${PRETTY_NAME:-unknown}" "${VERSION_CODENAME:-unknown}"
  printf 'root: %s on %s\n' "$(rootfs)" "$(findmnt -n -o SOURCE /)"
  printf 'systemd: %s\n' "$(systemctl is-system-running 2>/dev/null || echo unavailable)"
  printf 'interop: %s\n' "$(command -v powershell.exe >/dev/null && echo OK || echo missing)"
  [[ $(rootfs) == btrfs ]] && printf 'auto-update: %s\n' "$(systemctl is-enabled wsl-btrfs-update.timer 2>/dev/null || echo disabled)"
}

case ${1:-} in
  install) shift; install_root "${1:-$SIZE}";;
  update) update;;
  sync-bootstrap) sync_bootstrap;;
  compact) compact;;
  status) status;;
  *) usage;;
esac

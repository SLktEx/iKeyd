#!/usr/bin/env bash
# Sourced by wsl-btrfs-bench.sh; only the entry point may call these functions.
# Never format a Windows-attached disk. Format an owned loop seed, then convert it.
decode_windows_output() {
  python3 -c 'import sys; b=sys.stdin.buffer.read(); sys.stdout.write(b.decode("utf-16" if b.startswith((b"\xff\xfe",b"\xfe\xff")) else "utf-16le" if b"\0" in b else "utf-8", errors="replace"))'
}
windows_exec() {
  # Use WSL's interpreter directly: another distro can unregister global binfmt.
  # Inherit this caller's interop server and Windows token; never borrow a socket.
  local exe
  exe=$(command -v "$1") || return
  shift
  [[ -x /init && -f "$exe" ]] || return 1
  /init "$exe" "$exe" "$@"
}
windows_wsl() {
  # wsl.exe emits UTF-16LE when redirected. Preserve native failure via pipefail.
  windows_exec wsl.exe "$@" 2>&1 | decode_windows_output
}
wait_vhdx_device() {
  local v=$1 uuid dev rc deadline=$((SECONDS+15))
  uuid=$(cat "$WORK/$v.uuid")
  while (( SECONDS < deadline )); do
    if dev=$(root blkid -c /dev/null -t "UUID=$uuid" -o device); then
      [[ "$dev" =~ ^/dev/sd[a-z]+$ ]] || die "Ambiguous VHDX device for $uuid: $dev"
      printf '%s\n' "$dev" > "$WORK/$v.device"
      check_vhdx_device "$v" || die "VHDX UUID/size/file identity mismatch: $v"
      return 0
    else
      rc=$?
      (( rc == 2 )) || return "$rc"
    fi
    sleep 0.2
  done
  die "VHDX attached but UUID $uuid was not discovered within 15s; resource preserved"
}
check_vhdx_environment() {
  local cmd
  for cmd in wsl.exe powershell.exe wslpath qemu-img; do
    command -v "$cmd" >/dev/null || die "VHDX requires $cmd in PATH";
  done
  windows_exec powershell.exe -NoProfile -NonInteractive -Command 'exit (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))' || die 'VHDX attachment requires an elevated Windows terminal: launch WSL from PowerShell run as administrator'
  [[ -n "${VHDX_ROOT:-}" && -d "$VHDX_ROOT" && ! -L "$VHDX_ROOT" ]] || die 'Set VHDX_ROOT to an existing directory on a Windows drive, e.g. /mnt/c/Users/you/bench-vhdx'
  VHDX_ROOT=$(realpath -e "$VHDX_ROOT")
  [[ "$VHDX_ROOT" =~ ^/mnt/[a-z]/ && "$VHDX_ROOT" != *$'\n'* ]] || die 'VHDX_ROOT must be a Windows drive directory without newlines'
  [[ $(findmnt -rn -T "$VHDX_ROOT" -o FSTYPE) == 9p ]] || die 'VHDX_ROOT must be on the Windows drive (drvfs/9p)'
  check_vhdx_capacity
}
check_vhdx_capacity() {
  local available
  available=$(df -B1 --output=avail "$VHDX_ROOT" | tail -n 1)
  (( available >= IMAGE_GIB*1024*1024*1024 + 1024*1024*1024 )) || die "Insufficient Windows space for next VHDX: $available bytes free"
}
vhdx_file_identity() {
  # drvfs st_dev changes between Windows clients/mount namespaces; the drive
  # source and Windows file index remain stable. Keep the hardlink count too.
  [[ $(findmnt -rn -T "$1" -o FSTYPE) == 9p ]] || return 1
  printf '%s:%s\n' "$(findmnt -rn -T "$1" -o SOURCE)" "$(stat -c '%i:%h' -- "$1")"
}
validate_vhdx_path() {
  local v=$1 dir uuid
  [[ ! -L "$WORK/$v.vhdx-dir" && -f "$WORK/$v.vhdx-dir" ]] || return 1
  dir=$(cat "$WORK/$v.vhdx-dir") || return 1
  uuid=$(cat "$WORK/$v.uuid") || return 1
  [[ "$dir" =~ ^/mnt/[a-z]/ && "$dir" == */ikeyd-bench-vhdx-* && ! -L "$dir" && -d "$dir" ]] || return 1
  [[ $(realpath -e "$dir") == "$dir" && ! -L "$dir/owner" && $(cat "$dir/owner") == "$uuid" ]] || return 1
  [[ -f "$dir/disk.vhdx" && ! -L "$dir/disk.vhdx" ]] || return 1
  [[ $(vhdx_file_identity "$dir/disk.vhdx") == "$(cat "$WORK/$v.vhdx-id")" && $(cat "$WORK/$v.vhdx-id") == *:1 ]] || return 1
}
check_vhdx_device() {
  local v=$1 dev uuid matches
  validate_vhdx_path "$v" || return 1
  dev=$(cat "$WORK/$v.device") || return 1
  uuid=$(cat "$WORK/$v.uuid") || return 1
  [[ "$dev" =~ ^/dev/sd[a-z]+$ && -b "$dev" ]] || return 1
  [[ $(root blkid -p -s UUID -o value "$dev") == "$uuid" ]] || return 1
  [[ $(root blkid -p -s TYPE -o value "$dev") == "$(variant_fs "$v")" ]] || return 1
  [[ $(root blockdev --getsize64 "$dev") == "$(cat "$WORK/$v.size")" ]] || return 1
  matches=$(root blkid -c /dev/null -t "UUID=$uuid" -o device) || return 1
  [[ "$matches" == "$dev" ]] || return 1
}
setup_vhdx() {
  local v=$1 round=$2 img="$WORK/$1.img" loop identity uuid dir dev opts win
  check_vhdx_capacity
  (set -C; : > "$img")
  truncate -s "${IMAGE_GIB}G" "$img"
  identity=$(image_identity "$img")
  loop=$(root losetup --find --show --nooverlap "$img")
  printf '%s %s\n' "$loop" "$identity" > "$WORK/$v.state"
  sync -f "$WORK/$v.state"
  check_loop "$loop" "$img" "$identity" || die "VHDX seed ownership mismatch: $loop"
  if [[ "$(variant_fs "$v")" == ext4 ]]; then
    root mkfs.ext4 -q -m 0 -E nodiscard,lazy_itable_init=0,lazy_journal_init=0 "$loop"
  else
    root mkfs.btrfs -q -K "$loop"
  fi
  uuid=$(root blkid -s UUID -o value "$loop")
  [[ "$uuid" =~ ^[a-f0-9-]{36}$ ]] || die 'Missing VHDX seed UUID'
  printf '%s\n' "$uuid" > "$WORK/$v.uuid"
  printf '%s\n' "$((IMAGE_GIB*1024*1024*1024))" > "$WORK/$v.size"
  root losetup -d "$loop"
  wait_loop_detached "$loop" "$img" || die 'Seed loop detach pending'
  dir=$(mktemp -d "$VHDX_ROOT/ikeyd-bench-vhdx-XXXXXXXX")
  printf '%s\n' "$dir" > "$WORK/$v.vhdx-dir"
  printf '%s\n' "$uuid" > "$dir/owner"
  say "VHDX $v: converting new seed to dynamic VHDX at $dir/disk.vhdx"
  qemu-img convert -f raw -O vhdx -o subformat=dynamic,block_size=2097152 "$img" "$dir/disk.vhdx"
  printf '%s\n' "$(vhdx_file_identity "$dir/disk.vhdx")" > "$WORK/$v.vhdx-id"
  qemu-img info --output=json "$dir/disk.vhdx" > "$RESULTS/vhdx_${v}_round${round}_created.json"
  rm -- "$img" "$WORK/$v.state"
  win=$(wslpath -w "$dir/disk.vhdx")
  # Journal the exact owned Windows path before requesting attachment.
  printf '%s\n' "$win" > "$WORK/$v.attached"
  sync -f "$WORK/$v.attached"
  windows_wsl --mount --vhd "$win" --bare
  # UUID exists only on the newly created seed. Never guess from /dev/sdX order.
  wait_vhdx_device "$v"
  dev=$(cat "$WORK/$v.device")
  check_vhdx_device "$v" || die "Cannot uniquely identify attached VHDX: $v"
  no_mounts_under "$MNT_ROOT/$v" || die 'Unexpected mount before VHDX setup'
  if findmnt -rn -S "$dev" >/dev/null; then die "New VHDX already mounted elsewhere: $dev"; fi
  opts=$(mount_options "$v")
  record_device_options "$v" "$dev" "$round"
  root mkdir "$MNT_ROOT/$v"
  root mount -t "$(variant_fs "$v")" -o "$opts" "$dev" "$MNT_ROOT/$v"
  printf '%s\t%s\t%s\t%s\t%s\n' "$v" "$MNT_ROOT/$v" "$win" "$dev" "$round" >> "$WORK/mounts.tsv"
  cp "$WORK/mounts.tsv" "$RESULTS/mounts.tsv"
  assert_mount "$v"
  root chown "$(id -u):$(id -g)" "$MNT_ROOT/$v"
  record_storage "$v" "setup_round_$round"
}
record_vhdx_storage() {
  local v=$1 phase=$2 dir encoded_path
  validate_vhdx_path "$v" || die 'VHDX file identity changed'
  dir=$(cat "$WORK/$v.vhdx-dir")
  # Query Windows for allocated bytes; drvfs stat blocks are not authoritative.
  # qemu-img metadata is read only while the VHDX is detached.
  printf 'virtual_bytes_%s\t%s\nhost_file_length_%s\t%s\n' "$phase" "$(cat "$WORK/$v.size")" "$phase" "$(stat -c %s "$dir/disk.vhdx")" >> "$RESULTS/storage_$v.tsv"
  encoded_path=$(printf '%s' "$(wslpath -w "$dir/disk.vhdx")" | base64 -w0)
  windows_exec powershell.exe -NoProfile -NonInteractive -Command "& {
    \$ErrorActionPreference='Stop';
    Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class BenchAllocation { [DllImport(\"kernel32.dll\", CharSet=CharSet.Unicode, SetLastError=true, EntryPoint=\"GetCompressedFileSizeW\")] public static extern uint GetSize(string path, out uint high); }';
    \$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$encoded_path'));
    [uint32]\$hi=0; \$lo=[BenchAllocation]::GetSize(\$p,[ref]\$hi);
    if (\$lo -eq [uint32]::MaxValue -and [Runtime.InteropServices.Marshal]::GetLastWin32Error() -ne 0) { throw 'Host allocation query failed' };
    @{file_length=(Get-Item -LiteralPath \$p).Length; allocated_bytes=([uint64]\$hi*4294967296+[uint64]\$lo)} | ConvertTo-Json -Compress
  }" > "$RESULTS/vhdx_${v}_${phase}_host.json"
  if [[ "$(variant_fs "$v")" == btrfs ]]; then
    root btrfs filesystem usage -b "$MNT_ROOT/$v" > "$RESULTS/usage_${v}_${phase}.txt"
  else
    df -B1 "$MNT_ROOT/$v" > "$RESULTS/usage_${v}_${phase}.txt"
    df -i "$MNT_ROOT/$v" >> "$RESULTS/usage_${v}_${phase}.txt"
  fi
}
cleanup_vhdx() {
  local v=$1 dir dev uuid source fs found_uuid loop expected attached attempt
  # An interrupted seed setup is also owned, but never detach a reused loop.
  if [[ -f "$WORK/$v.state" ]]; then
    read -r loop expected < "$WORK/$v.state" || return 1
    [[ $(image_identity "$WORK/$v.img") == "$expected" ]] || return 1
    attached=$(root losetup -j "$WORK/$v.img" --noheadings --output NAME) || return 1
    if [[ -n "$attached" ]]; then
      check_loop "$loop" "$WORK/$v.img" "$expected" || return 1
      if findmnt -rn -S "$loop" >/dev/null; then return 1; fi
      root losetup -d "$loop" || return 1
      wait_loop_detached "$loop" "$WORK/$v.img" || return 1
    fi
    rm -- "$WORK/$v.img" "$WORK/$v.state" || return 1
  elif [[ -e "$WORK/$v.img" ]]; then say "Unjournaled VHDX seed: $v"; return 1; fi
  if [[ -f "$WORK/$v.vhdx-dir" ]]; then
    validate_vhdx_path "$v" || { say "VHDX ownership incomplete/changed; preserve $v for inspection"; return 1; }
    dir=$(cat "$WORK/$v.vhdx-dir") || return 1
    if [[ -f "$WORK/$v.attached" ]]; then
      if [[ ! -s "$WORK/$v.device" ]]; then wait_vhdx_device "$v" || return 1; fi
      check_vhdx_device "$v" || { say "VHDX attachment ambiguous; preserve $v and inspect exact path in $WORK/$v.attached"; return 1; }
      dev=$(cat "$WORK/$v.device") || return 1
      uuid=$(cat "$WORK/$v.uuid") || return 1
      if mountpoint -q "$MNT_ROOT/$v"; then
        read -r source fs found_uuid < <(findmnt -rn -M "$MNT_ROOT/$v" -o SOURCE,FSTYPE,UUID)
        [[ "$source" == "$dev" && "$fs" == "$(variant_fs "$v")" && "$found_uuid" == "$uuid" ]] || return 1
        root umount -- "$MNT_ROOT/$v" || { say "Busy VHDX mount preserved: $MNT_ROOT/$v"; return 1; }
      fi
      no_mounts_under "$MNT_ROOT/$v" || return 1
      if findmnt -rn -S "$dev" >/dev/null; then say "VHDX mounted elsewhere: $dev"; return 1; fi
      [[ $(cat "$WORK/$v.attached") == "$(wslpath -w "$dir/disk.vhdx")" ]] || return 1
      windows_wsl --unmount "$(cat "$WORK/$v.attached")" || return 1
      for ((attempt=0; attempt<50; attempt++)); do
        [[ -b "$dev" ]] || break
        sleep 0.1
      done
      [[ ! -b "$dev" ]] || { say "VHDX detach pending; preserved: $dev"; return 1; }
      rm -- "$WORK/$v.attached" || return 1
    fi
    qemu-img info --output=json "$dir/disk.vhdx" > "$RESULTS/vhdx_${v}_detached_$(date +%s%N).json" || return 1
    rm -- "$dir/disk.vhdx" "$dir/owner" || return 1
    rmdir -- "$dir" || return 1
  fi
  no_mounts_under "$MNT_ROOT/$v" || return 1
  [[ ! -d "$MNT_ROOT/$v" ]] || root rmdir -- "$MNT_ROOT/$v" || return 1
  rm -f -- "$WORK/$v.vhdx-dir" "$WORK/$v.vhdx-id" "$WORK/$v.device" "$WORK/$v.uuid" "$WORK/$v.size" || return 1
}

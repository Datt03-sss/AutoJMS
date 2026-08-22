#!/usr/bin/env bash
#
# Bootstrap VPS cho DataHub — tự động hoá Pha 0 và Pha 1 của
# DEPLOY_EXECUTION_CHECKLIST.vi.md (tương ứng bước 1–3 của VPS_DEPLOY_GUIDE.vi.md).
#
# Làm gì:
#   - cập nhật hệ thống + bật vá bảo mật tự động
#   - kiểm libseccomp2 (điều kiện để image .NET 10 chạy được trên host cũ)
#   - timezone Asia/Ho_Chi_Minh + hostname + đồng bộ NTP (lease fencing cần đồng hồ đúng)
#   - tạo user không phải root, có sudo
#   - UFW chỉ mở 22/80/443, fail2ban cho sshd
#   - Docker Engine từ repo chính thức + PowerShell 7
#   - (tuỳ chọn, có khoá SSH) khoá SSH về key-only, chặn đăng nhập root bằng mật khẩu
#
# KHÔNG làm gì: không tạo secret, không tạo file .env, không clone repo, không chạm database.
# Script này không chứa và không in ra bất kỳ mật khẩu / khoá nào.
#
# Chạy lại nhiều lần an toàn (idempotent).
#
# Cách dùng — chạy bằng root trên VPS:
#   bash bootstrap-vps.sh --hostname datahub-staging --user datahub
#   bash bootstrap-vps.sh --hostname datahub-staging --user datahub --harden-ssh
#
set -euo pipefail

APP_USER="datahub"
NEW_HOSTNAME=""
HARDEN_SSH="no"
ASSUME_YES="no"
TIMEZONE="Asia/Ho_Chi_Minh"
MIN_SECCOMP="2.5.1"

usage() {
    cat <<'EOF'
Cách dùng: bash bootstrap-vps.sh [tuỳ chọn]

  --user NAME        User vận hành sẽ tạo (mặc định: datahub)
  --hostname NAME    Đặt hostname (ví dụ: datahub-staging). Bỏ trống thì giữ nguyên.
  --harden-ssh       Khoá SSH về key-only + chặn root đăng nhập bằng mật khẩu.
                     CHỈ áp dụng khi user đã có authorized_keys — nếu chưa có,
                     script bỏ qua bước này và in hướng dẫn, không tự khoá cửa.
  --timezone TZ      Mặc định Asia/Ho_Chi_Minh
  --yes              Không hỏi xác nhận
  -h | --help        In trợ giúp này
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --user)      APP_USER="${2:?--user cần giá trị}"; shift 2 ;;
        --hostname)  NEW_HOSTNAME="${2:?--hostname cần giá trị}"; shift 2 ;;
        --timezone)  TIMEZONE="${2:?--timezone cần giá trị}"; shift 2 ;;
        --harden-ssh) HARDEN_SSH="yes"; shift ;;
        --yes)       ASSUME_YES="yes"; shift ;;
        -h|--help)   usage; exit 0 ;;
        *) echo "Tham số không hiểu: $1" >&2; usage >&2; exit 2 ;;
    esac
done

log()  { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
ok()   { printf '    \033[0;32m✔\033[0m %s\n' "$*"; }
warn() { printf '    \033[0;33m!\033[0m %s\n' "$*"; }
die()  { printf '\n\033[0;31m✖ %s\033[0m\n' "$*" >&2; exit 1; }

[[ ${EUID} -eq 0 ]] || die "Phải chạy bằng root (hoặc sudo)."
[[ -r /etc/os-release ]] || die "Không đọc được /etc/os-release — script chỉ hỗ trợ Ubuntu."

# shellcheck disable=SC1091
. /etc/os-release
[[ "${ID:-}" == "ubuntu" ]] || die "Chỉ hỗ trợ Ubuntu, phát hiện ID=${ID:-?}."

UBUNTU_VERSION="${VERSION_ID:-?}"
UBUNTU_CODENAME_LOCAL="${VERSION_CODENAME:-}"
[[ -n "$UBUNTU_CODENAME_LOCAL" ]] || die "Không xác định được VERSION_CODENAME."

log "Môi trường phát hiện được"
printf '    Ubuntu       : %s (%s)\n' "$UBUNTU_VERSION" "$UBUNTU_CODENAME_LOCAL"
printf '    Kernel       : %s\n' "$(uname -r)"
printf '    RAM          : %s\n' "$(free -h | awk '/^Mem:/{print $2}')"
printf '    Disk (/)     : %s trống\n' "$(df -h --output=avail / | tail -1 | tr -d ' ')"
printf '    User sẽ tạo  : %s\n' "$APP_USER"

case "$UBUNTU_VERSION" in
    24.04|22.04) ok "Phiên bản còn trong hỗ trợ tiêu chuẩn." ;;
    20.04)
        warn "Ubuntu 20.04 đã HẾT hỗ trợ tiêu chuẩn (05/2025) — chỉ còn vá qua Ubuntu Pro/ESM."
        warn "Docker CE cho focal dừng ở 28.1.1; noble (24.04) đang ở nhánh 29.x."
        warn "Khuyến nghị: cài lại VPS bằng Ubuntu 24.04 LTS trước khi triển khai thật."
        ;;
    *) warn "Chưa kiểm chứng trên Ubuntu $UBUNTU_VERSION — tiếp tục nhưng hãy soi kỹ từng bước." ;;
esac

RAM_MB="$(free -m | awk '/^Mem:/{print $2}')"
[[ "$RAM_MB" -ge 3500 ]] || warn "RAM ${RAM_MB} MB < 4 GB: postgres(2G)+api(768M)+caddy(256M) sẽ chật."
DISK_GB="$(df -BG --output=avail / | tail -1 | tr -dc '0-9')"
[[ "$DISK_GB" -ge 20 ]] || warn "Còn ${DISK_GB} GB trống: build image trên VPS cần ~10 GB cho SDK + cache."

if [[ "$ASSUME_YES" != "yes" ]]; then
    printf '\nTiếp tục? [y/N] '
    read -r reply
    [[ "$reply" == "y" || "$reply" == "Y" ]] || die "Đã huỷ."
fi

export DEBIAN_FRONTEND=noninteractive

# ── 1. Cập nhật hệ thống + vá tự động ─────────────────────────────────────────
log "Cập nhật hệ thống và bật vá bảo mật tự động"
apt-get update -qq
apt-get upgrade -y -qq
apt-get install -y -qq ca-certificates curl gnupg lsb-release ufw fail2ban unattended-upgrades apt-transport-https
dpkg-reconfigure -f noninteractive unattended-upgrades
ok "unattended-upgrades đã cài và bật"

# ── 2. libseccomp — điều kiện để image nền mới chạy trên host cũ ───────────────
# glibc >= 2.34 (image .NET 10 / alpine mới) gọi clone3. libseccomp < 2.5 trả
# EPERM thay vì ENOSYS nên container mới sẽ chết với "Operation not permitted".
log "Kiểm libseccomp2 (>= ${MIN_SECCOMP})"
SECCOMP_VER="$(dpkg-query -W -f='${Version}' libseccomp2 2>/dev/null || true)"
if [[ -z "$SECCOMP_VER" ]]; then
    warn "Chưa có libseccomp2 — Docker sẽ kéo về cùng lúc cài."
elif dpkg --compare-versions "$SECCOMP_VER" ge "$MIN_SECCOMP"; then
    ok "libseccomp2 $SECCOMP_VER"
else
    warn "libseccomp2 $SECCOMP_VER quá cũ. Thử nâng từ ${UBUNTU_CODENAME_LOCAL}-updates…"
    apt-get install -y -qq -t "${UBUNTU_CODENAME_LOCAL}-updates" libseccomp2 || true
    SECCOMP_VER="$(dpkg-query -W -f='${Version}' libseccomp2 2>/dev/null || true)"
    dpkg --compare-versions "$SECCOMP_VER" ge "$MIN_SECCOMP" \
        || die "libseccomp2 vẫn là $SECCOMP_VER (< $MIN_SECCOMP). Container .NET 10 sẽ lỗi 'Operation not permitted'. Nâng gói này trước khi tiếp tục."
    ok "libseccomp2 nâng lên $SECCOMP_VER"
fi

# ── 3. Timezone, hostname, đồng hồ ────────────────────────────────────────────
log "Timezone, hostname và đồng bộ đồng hồ"
timedatectl set-timezone "$TIMEZONE"
timedatectl set-ntp true || warn "Không bật được NTP tự động — kiểm systemd-timesyncd bằng tay."
if [[ -n "$NEW_HOSTNAME" ]]; then
    hostnamectl set-hostname "$NEW_HOSTNAME"
    grep -qE "^127\.0\.1\.1[[:space:]]+${NEW_HOSTNAME}\b" /etc/hosts \
        || printf '127.0.1.1\t%s\n' "$NEW_HOSTNAME" >> /etc/hosts
    ok "hostname = $NEW_HOSTNAME"
fi
# Lease fencing dùng clock_timestamp() của PostgreSQL: đồng hồ lệch ⇒ fence sai.
if timedatectl show -p NTPSynchronized --value 2>/dev/null | grep -qi yes; then
    ok "Đồng hồ đã đồng bộ NTP"
else
    warn "Đồng hồ CHƯA đồng bộ. Chờ vài phút rồi kiểm lại: timedatectl"
fi

# ── 4. User vận hành ──────────────────────────────────────────────────────────
log "User vận hành '$APP_USER'"
if id -u "$APP_USER" >/dev/null 2>&1; then
    ok "User đã tồn tại"
else
    adduser --disabled-password --gecos "" "$APP_USER"
    ok "Đã tạo user (không đặt mật khẩu — đăng nhập bằng khoá SSH)"
fi
usermod -aG sudo "$APP_USER"

# User không có mật khẩu thì 'sudo' sẽ hỏi một mật khẩu không tồn tại ⇒ mất quyền
# sudo. Cách cloud image của Ubuntu xử lý: NOPASSWD. An toàn tương đương ở đây vì
# SSH đã là key-only — ai có khoá thì đã vào được tài khoản.
# Muốn sudo hỏi mật khẩu: chạy 'passwd <user>' rồi xoá file drop-in dưới đây.
SUDOERS_FILE="/etc/sudoers.d/90-datahub-${APP_USER}"
if [[ ! -f "$SUDOERS_FILE" ]]; then
    printf '%s ALL=(ALL) NOPASSWD:ALL\n' "$APP_USER" > "$SUDOERS_FILE"
    chmod 440 "$SUDOERS_FILE"
    if visudo -cf "$SUDOERS_FILE" >/dev/null; then
        ok "sudo NOPASSWD cho $APP_USER (xem ghi chú trong script nếu muốn đổi)"
    else
        rm -f "$SUDOERS_FILE"
        die "File sudoers sinh ra không hợp lệ — đã xoá, không áp dụng."
    fi
fi
USER_HOME="$(getent passwd "$APP_USER" | cut -d: -f6)"
install -d -m 700 -o "$APP_USER" -g "$APP_USER" "$USER_HOME/.ssh"
# Kế thừa khoá của root để không mất đường vào; không ghi đè khoá đã có của user.
if [[ -s /root/.ssh/authorized_keys ]]; then
    touch "$USER_HOME/.ssh/authorized_keys"
    while IFS= read -r key; do
        [[ -n "$key" ]] || continue
        grep -qxF "$key" "$USER_HOME/.ssh/authorized_keys" || printf '%s\n' "$key" >> "$USER_HOME/.ssh/authorized_keys"
    done < /root/.ssh/authorized_keys
    chown "$APP_USER:$APP_USER" "$USER_HOME/.ssh/authorized_keys"
    chmod 600 "$USER_HOME/.ssh/authorized_keys"
    ok "Đã sao chép khoá SSH của root sang $APP_USER"
else
    warn "root chưa có authorized_keys — $APP_USER cũng chưa có khoá nào."
fi

# ── 5. Firewall ───────────────────────────────────────────────────────────────
log "UFW — chỉ mở 22, 80, 443"
ufw --force default deny incoming >/dev/null
ufw --force default allow outgoing >/dev/null
ufw allow 22/tcp  >/dev/null   # cho phép SSH TRƯỚC khi enable, tránh tự khoá cửa
ufw allow 80/tcp  >/dev/null   # ACME HTTP-01 của Caddy
ufw allow 443/tcp >/dev/null
ufw --force enable >/dev/null
ok "$(ufw status | head -1)"

# ── 6. fail2ban ───────────────────────────────────────────────────────────────
log "fail2ban cho sshd"
if [[ ! -f /etc/fail2ban/jail.local ]]; then
    cat > /etc/fail2ban/jail.local <<'EOF'
[DEFAULT]
bantime  = 1h
findtime = 10m
maxretry = 5
backend  = systemd

[sshd]
enabled = true
EOF
    ok "Đã tạo /etc/fail2ban/jail.local"
else
    ok "jail.local đã có — giữ nguyên cấu hình hiện tại"
fi
systemctl enable --now fail2ban >/dev/null 2>&1 || warn "Không bật được fail2ban — kiểm 'systemctl status fail2ban'."

# ── 7. Docker Engine từ repo chính thức ───────────────────────────────────────
log "Docker Engine"
if command -v docker >/dev/null 2>&1; then
    ok "$(docker --version)"
else
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL "https://download.docker.com/linux/ubuntu/gpg" -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc
    printf 'deb [arch=%s signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu %s stable\n' \
        "$(dpkg --print-architecture)" "$UBUNTU_CODENAME_LOCAL" > /etc/apt/sources.list.d/docker.list
    apt-get update -qq
    apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
    ok "$(docker --version)"
fi
systemctl enable --now docker >/dev/null 2>&1 || true
usermod -aG docker "$APP_USER"
ok "$(docker compose version 2>/dev/null || echo 'docker compose: KHÔNG có — kiểm docker-compose-plugin')"
ok "Đã thêm $APP_USER vào group docker (cần đăng nhập lại mới có hiệu lực)"

# ── 8. PowerShell 7 ───────────────────────────────────────────────────────────
# Bắt buộc: restore-postgres.ps1 dùng cú pháp PS7, PowerShell 5.1 không parse được.
log "PowerShell 7"
if command -v pwsh >/dev/null 2>&1; then
    ok "$(pwsh --version)"
else
    tmp_deb="$(mktemp --suffix=.deb)"
    if curl -fsSL "https://packages.microsoft.com/config/ubuntu/${UBUNTU_VERSION}/packages-microsoft-prod.deb" -o "$tmp_deb"; then
        dpkg -i "$tmp_deb" >/dev/null
        rm -f "$tmp_deb"
        apt-get update -qq
        apt-get install -y -qq powershell && ok "$(pwsh --version)" \
            || warn "Không cài được powershell từ repo Microsoft. Xem §3.2 của VPS_DEPLOY_GUIDE.vi.md."
    else
        rm -f "$tmp_deb"
        warn "Microsoft chưa có repo cho Ubuntu ${UBUNTU_VERSION}. Cài PowerShell theo §3.2 bằng tay."
    fi
fi

# ── 9. Khoá SSH (tuỳ chọn, có bảo hiểm chống tự khoá cửa) ─────────────────────
log "Khoá SSH về key-only"
if [[ "$HARDEN_SSH" != "yes" ]]; then
    warn "Bỏ qua (không truyền --harden-ssh)."
    warn "Từ MÁY DEV: ssh-copy-id ${APP_USER}@<ip>  → rồi chạy lại script với --harden-ssh"
elif [[ ! -s "$USER_HOME/.ssh/authorized_keys" ]]; then
    warn "TỪ CHỐI khoá SSH: $APP_USER chưa có authorized_keys."
    warn "Khoá lúc này là tự đẩy mình ra khỏi máy. Cài khoá trước:"
    warn "  ssh-copy-id ${APP_USER}@<ip>"
else
    install -d -m 755 /etc/ssh/sshd_config.d
    # sshd lấy giá trị của lần khai báo ĐẦU TIÊN. Trên 20.04 file gốc không có
    # dòng Include nào, nên phải chèn Include lên ĐẦU file để drop-in thắng.
    if ! grep -qE '^\s*Include\s+/etc/ssh/sshd_config\.d/\*\.conf' /etc/ssh/sshd_config; then
        cp -n /etc/ssh/sshd_config /etc/ssh/sshd_config.bak-datahub
        { printf 'Include /etc/ssh/sshd_config.d/*.conf\n\n'; cat /etc/ssh/sshd_config; } > /etc/ssh/sshd_config.new
        mv /etc/ssh/sshd_config.new /etc/ssh/sshd_config
        ok "Đã chèn Include vào đầu sshd_config (bản gốc: sshd_config.bak-datahub)"
    fi
    cat > /etc/ssh/sshd_config.d/60-datahub-hardening.conf <<'EOF'
# DataHub hardening — xem VPS_DEPLOY_GUIDE.vi.md §2.2
PasswordAuthentication no
KbdInteractiveAuthentication no
ChallengeResponseAuthentication no
PermitRootLogin prohibit-password
PubkeyAuthentication yes
X11Forwarding no
MaxAuthTries 3
EOF
    if sshd -t; then
        systemctl reload ssh 2>/dev/null || systemctl reload sshd 2>/dev/null || systemctl restart ssh
        ok "SSH đã về key-only, root không đăng nhập được bằng mật khẩu"
        warn "GIỮ NGUYÊN session này. Mở session MỚI để xác nhận vào được rồi mới đóng session cũ."
    else
        rm -f /etc/ssh/sshd_config.d/60-datahub-hardening.conf
        die "sshd -t báo cấu hình sai — đã rollback, sshd không bị reload."
    fi
fi

# ── 10. Kiểm chứng bề mặt tấn công ────────────────────────────────────────────
log "Bề mặt tấn công đang mở (§2.5)"
ss -tulpn | awk 'NR==1 || /LISTEN/' | grep -vE '127\.0\.0\.1|\[::1\]' || true
if ss -tulpn | grep -qE ':5432\b'; then
    warn "PHÁT HIỆN 5432 đang listen — PostgreSQL không bao giờ được publish ra host. Dừng lại và kiểm."
else
    ok "5432 không lộ ra ngoài"
fi

log "Xong Pha 0 + Pha 1"
cat <<EOF

    Tiếp theo (DEPLOY_EXECUTION_CHECKLIST.vi.md):
      1. Từ máy dev: ssh-copy-id ${APP_USER}@<ip>, rồi chạy lại script với --harden-ssh
      2. Đăng nhập bằng '${APP_USER}' (để có group docker), kiểm: docker run --rm hello-world
      3. Pha 2 — trỏ A record về IP này TRƯỚC khi bật Caddy
      4. Pha 3 — clone repo rồi build image (§5.0), push, lấy digest
      5. Pha 4 — sinh secret (§7.1), tạo .env.staging, start-stack.ps1

    Chưa làm và CỐ Ý không làm: không sinh secret, không tạo .env, không clone repo.
EOF

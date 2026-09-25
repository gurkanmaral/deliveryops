#!/usr/bin/env bash

set -euo pipefail

CORE_API_URL="${CORE_API_URL:-http://localhost:5100}"
AUTH_API_URL="${AUTH_API_URL:-http://localhost:5200}"
NOTIFICATIONS_API_URL="${NOTIFICATIONS_API_URL:-http://localhost:5300}"
INTEGRATIONS_API_URL="${INTEGRATIONS_API_URL:-http://localhost:5400}"
ADMIN_WEB_URL="${ADMIN_WEB_URL:-http://localhost:5173}"

response_file="$(mktemp -t deliveryops-smoke-response.XXXXXX)"
cookie_file="$(mktemp -t deliveryops-smoke-cookie.XXXXXX)"
registered_token=""
courier_token=""
mobile_refresh_token=""
passed=0

cleanup() {
  if [[ -n "$registered_token" && -n "$courier_token" ]]; then
    local payload
    payload="$(jq -nc --arg token "$registered_token" '{expoPushToken:$token}')"
    curl -sS -o /dev/null -X DELETE \
      -H "Authorization: Bearer $courier_token" \
      -H 'Content-Type: application/json' \
      -d "$payload" "$NOTIFICATIONS_API_URL/api/v1/devices" || true
  fi
  if [[ -n "$mobile_refresh_token" ]]; then
    local payload
    payload="$(jq -nc --arg token "$mobile_refresh_token" '{refreshToken:$token}')"
    curl -sS -o /dev/null -H 'Content-Type: application/json' \
      -d "$payload" "$AUTH_API_URL/api/v1/auth/mobile/logout" || true
  fi
  rm -f "$response_file" "$cookie_file"
}
trap cleanup EXIT

for command_name in curl jq; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'Eksik bağımlılık: %s\n' "$command_name" >&2
    exit 1
  fi
done

pass() {
  passed=$((passed + 1))
  printf '  ✓ %s\n' "$1"
}

request() {
  local label="$1"
  local expected="$2"
  local method="$3"
  local url="$4"
  local token="${5:-}"
  local payload="${6:-}"
  local args=(-sS -o "$response_file" -w '%{http_code}' -X "$method")
  if [[ -n "$token" ]]; then args+=(-H "Authorization: Bearer $token"); fi
  if [[ -n "$payload" ]]; then args+=(-H 'Content-Type: application/json' -d "$payload"); fi
  local status
  status="$(curl "${args[@]}" "$url")"
  if [[ "$status" != "$expected" ]]; then
    printf '  ✗ %s: HTTP %s bekleniyordu, %s döndü.\n' "$label" "$expected" "$status" >&2
    sed -n '1,20p' "$response_file" >&2
    exit 1
  fi
  pass "$label"
}

assert_response() {
  local label="$1"
  local filter="$2"
  if ! jq -e "$filter" "$response_file" >/dev/null; then
    printf '  ✗ %s: cevap beklenen JSON koşulunu sağlamadı.\n' "$label" >&2
    sed -n '1,20p' "$response_file" >&2
    exit 1
  fi
  pass "$label"
}

web_login() {
  local email="$1"
  local password="$2"
  local payload
  payload="$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password}')"
  local status
  status="$(curl -sS -o "$response_file" -w '%{http_code}' -c "$cookie_file" \
    -H 'Content-Type: application/json' -d "$payload" "$AUTH_API_URL/api/v1/auth/login")"
  if [[ "$status" != '200' ]]; then
    printf '  ✗ %s hesabı ile giriş başarısız: HTTP %s\n' "$email" "$status" >&2
    sed -n '1,20p' "$response_file" >&2
    exit 1
  fi
  jq -er '.accessToken' "$response_file"
}

web_logout() {
  curl -sS -o /dev/null -b "$cookie_file" -X POST "$AUTH_API_URL/api/v1/auth/logout"
  : > "$cookie_file"
}

printf 'DeliveryOps local smoke testi\n\n'
printf 'Servisler\n'
request 'Core API sağlık kontrolü' 200 GET "$CORE_API_URL/health"
request 'Auth API sağlık kontrolü' 200 GET "$AUTH_API_URL/health"
request 'Notifications API sağlık kontrolü' 200 GET "$NOTIFICATIONS_API_URL/health"
request 'Integrations API sağlık kontrolü' 200 GET "$INTEGRATIONS_API_URL/health"
request 'Admin panel erişimi' 200 GET "$ADMIN_WEB_URL/"
request 'Core Swagger sözleşmesi' 200 GET "$CORE_API_URL/swagger/v1/swagger.json"
request 'Auth Swagger sözleşmesi' 200 GET "$AUTH_API_URL/swagger/v1/swagger.json"
request 'Notifications Swagger sözleşmesi' 200 GET "$NOTIFICATIONS_API_URL/swagger/v1/swagger.json"
request 'Integrations Swagger sözleşmesi' 200 GET "$INTEGRATIONS_API_URL/swagger/v1/swagger.json"
request 'Eksik giriş bilgisi güvenli biçimde reddedilir' 400 POST "$AUTH_API_URL/api/v1/auth/login" '' '{"email":null,"password":null}'

printf '\nPlatform yöneticisi\n'
admin_token="$(web_login 'admin@deliveryops.local' 'DeliveryOps123!')"
pass 'Platform yöneticisi girişi'
request 'Platform yöneticisi kimliği' 200 GET "$AUTH_API_URL/api/v1/auth/me" "$admin_token"
assert_response 'PlatformAdmin rolü' '.roles | index("PlatformAdmin") != null'
request 'Genel dashboard' 200 GET "$CORE_API_URL/api/v1/dashboard/summary" "$admin_token"
request 'İşletme listesi' 200 GET "$CORE_API_URL/api/v1/businesses?page=1&pageSize=5" "$admin_token"
request 'Kurye listesi' 200 GET "$CORE_API_URL/api/v1/couriers?page=1&pageSize=5" "$admin_token"
request 'Sipariş listesi' 200 GET "$CORE_API_URL/api/v1/orders?page=1&pageSize=5" "$admin_token"
request 'Kredi paketleri' 200 GET "$CORE_API_URL/api/v1/credits/packages" "$admin_token"
request 'Mutabakat listesi' 200 GET "$CORE_API_URL/api/v1/billing/settlements?page=1&pageSize=5" "$admin_token"
request 'Operasyon alarmları' 200 GET "$CORE_API_URL/api/v1/operations/alerts?page=1&pageSize=5" "$admin_token"
request 'Entegrasyon dead-letter listesi' 200 GET "$CORE_API_URL/api/v1/operations/integration-outbox" "$admin_token"
request 'Bildirim dead-letter listesi' 200 GET "$CORE_API_URL/api/v1/operations/notification-outbox" "$admin_token"
request 'Entegrasyon listesi' 200 GET "$INTEGRATIONS_API_URL/api/v1/integrations" "$admin_token"
web_logout

printf '\nİşletme yöneticisi\n'
business_token="$(web_login 'business@demo.local' 'BusinessDemo123!')"
pass 'İşletme yöneticisi girişi'
request 'İşletme yöneticisi kimliği' 200 GET "$AUTH_API_URL/api/v1/auth/me" "$business_token"
assert_response 'BusinessAdmin rolü' '.roles | index("BusinessAdmin") != null'
request 'Tenant dashboard' 200 GET "$CORE_API_URL/api/v1/dashboard/summary" "$business_token"
request 'Tenant şubeleri' 200 GET "$CORE_API_URL/api/v1/branches?page=1&pageSize=10" "$business_token"
request 'Tenant kuryeleri' 200 GET "$CORE_API_URL/api/v1/couriers?page=1&pageSize=10" "$business_token"
request 'Tenant siparişleri' 200 GET "$CORE_API_URL/api/v1/orders?page=1&pageSize=10" "$business_token"
request 'Tenant kredi hesabı' 200 GET "$CORE_API_URL/api/v1/credits/account" "$business_token"
request 'Tenant entegrasyonları' 200 GET "$INTEGRATIONS_API_URL/api/v1/integrations" "$business_token"
web_logout

printf '\nİşletme personeli ve yetkilendirme\n'
staff_token="$(web_login 'staff@demo.local' 'StaffDemo123!')"
pass 'İşletme personeli girişi'
request 'Personel sipariş okuma' 200 GET "$CORE_API_URL/api/v1/orders?page=1&pageSize=5" "$staff_token"
request 'Personel entegrasyon okuma' 200 GET "$INTEGRATIONS_API_URL/api/v1/integrations" "$staff_token"
request 'Personel işletme oluşturamaz' 403 POST "$CORE_API_URL/api/v1/businesses" "$staff_token" '{}'
web_logout

printf '\nKurye mobil akışı\n'
mobile_payload="$(jq -nc --arg email 'courier@demo.local' --arg password 'CourierDemo123!' '{email:$email,password:$password}')"
request 'Kurye mobil girişi' 200 POST "$AUTH_API_URL/api/v1/auth/mobile/login" '' "$mobile_payload"
courier_token="$(jq -er '.accessToken' "$response_file")"
mobile_refresh_token="$(jq -er '.refreshToken' "$response_file")"
request 'Kurye kimliği' 200 GET "$AUTH_API_URL/api/v1/auth/me" "$courier_token"
assert_response 'Courier rolü' '.roles | index("Courier") != null'
request 'Kurye profili' 200 GET "$CORE_API_URL/api/v1/couriers/me" "$courier_token"
request 'Kurye paketleri' 200 GET "$CORE_API_URL/api/v1/orders?page=1&pageSize=10" "$courier_token"
request 'Üstlenilebilir paketler' 200 GET "$CORE_API_URL/api/v1/orders/available" "$courier_token"
request 'Kurye işletme yönetimine erişemez' 403 GET "$CORE_API_URL/api/v1/businesses?page=1&pageSize=5" "$courier_token"
request 'Kurye entegrasyon dead-letter listesine erişemez' 403 GET "$CORE_API_URL/api/v1/operations/integration-outbox" "$courier_token"
request 'Kurye bildirim dead-letter listesine erişemez' 403 GET "$CORE_API_URL/api/v1/operations/notification-outbox" "$courier_token"

registered_token="ExpoPushToken[deliveryops-local-smoke-$(date +%s)]"
device_payload="$(jq -nc --arg token "$registered_token" '{expoPushToken:$token,platform:"ios",deviceName:"Local Smoke Test"}')"
request 'Push cihazı kaydı' 200 POST "$NOTIFICATIONS_API_URL/api/v1/devices" "$courier_token" "$device_payload"
request 'Push cihazı listesi' 200 GET "$NOTIFICATIONS_API_URL/api/v1/devices/me" "$courier_token"
assert_response 'Test cihazı aktif listede' 'map(select(.deviceName == "Local Smoke Test")) | length == 1'
delete_payload="$(jq -nc --arg token "$registered_token" '{expoPushToken:$token}')"
request 'Push cihazı kaydını kaldırma' 204 DELETE "$NOTIFICATIONS_API_URL/api/v1/devices" "$courier_token" "$delete_payload"
registered_token=""
request 'Push cihazı kaldırıldı' 200 GET "$NOTIFICATIONS_API_URL/api/v1/devices/me" "$courier_token"
assert_response 'Test cihazı aktif listeden çıktı' 'map(select(.deviceName == "Local Smoke Test")) | length == 0'

logout_payload="$(jq -nc --arg token "$mobile_refresh_token" '{refreshToken:$token}')"
request 'Kurye mobil çıkışı' 204 POST "$AUTH_API_URL/api/v1/auth/mobile/logout" '' "$logout_payload"
mobile_refresh_token=""

printf '\n%d kontrol başarıyla tamamlandı.\n' "$passed"

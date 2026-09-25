#!/usr/bin/env bash

set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ -s "$HOME/.nvm/nvm.sh" ]]; then
  # shellcheck source=/dev/null
  source "$HOME/.nvm/nvm.sh"
  nvm use "$(cat "$root_dir/apps/courier-mobile/.nvmrc")" >/dev/null
fi

ensure_node_modules() {
  local directory="$1"
  if [[ ! -d "$directory/node_modules" ]]; then
    (cd "$directory" && npm ci)
  fi
}

run_step() {
  local title="$1"
  shift
  printf '\n==> %s\n' "$title"
  "$@"
}

run_in_dir() {
  local title="$1"
  local directory="$2"
  shift 2
  printf '\n==> %s\n' "$title"
  (cd "$directory" && "$@")
}

run_step 'Backend unit ve integration testleri' dotnet test "$root_dir/backend/DeliveryOps.sln" --nologo
run_step 'POS bridge unit testleri' dotnet test "$root_dir/apps/pos-bridge/tests/DeliveryOps.PosBridge.Core.Tests/DeliveryOps.PosBridge.Core.Tests.csproj" --nologo

ensure_node_modules "$root_dir/apps/admin-web"
run_step 'Admin panel lint' npm --prefix "$root_dir/apps/admin-web" run lint
run_step 'Admin panel production build' npm --prefix "$root_dir/apps/admin-web" run build
if [[ "${SKIP_E2E:-0}" != '1' ]]; then
  run_step 'Admin panel Playwright E2E' npm --prefix "$root_dir/apps/admin-web" run test:e2e
fi

ensure_node_modules "$root_dir/apps/courier-mobile"
run_step 'Mobil Expo Doctor' npm --prefix "$root_dir/apps/courier-mobile" run doctor
run_step 'Mobil TypeScript kontrolü' npm --prefix "$root_dir/apps/courier-mobile" run typecheck
run_step 'Mobil lint' npm --prefix "$root_dir/apps/courier-mobile" run lint
run_in_dir 'Mobil web export' "$root_dir/apps/courier-mobile" npx expo export --platform web

if [[ "${SKIP_SMOKE:-0}" != '1' ]]; then
  run_step 'Çalışan local sistem smoke testi' "$root_dir/scripts/local-smoke.sh"
fi

printf '\nTüm local kalite kontrolleri başarılı.\n'

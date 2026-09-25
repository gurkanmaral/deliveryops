DOCKER_DESKTOP_BIN ?= /Applications/Docker.app/Contents/Resources/bin
DOCKER := PATH="$(DOCKER_DESKTOP_BIN):$(PATH)" docker

.PHONY: infra stack stack-build stack-down migrate seed smoke e2e webhook-e2e webhook-yemeksepeti webhook-getir mobile-e2e check test

infra:
	$(DOCKER) compose up -d --wait

stack:
	$(DOCKER) compose --profile application up -d --wait

stack-build:
	$(DOCKER) compose --profile application up -d --build --wait

stack-down:
	$(DOCKER) compose --profile application down

migrate:
	cd backend && dotnet tool restore
	cd backend && dotnet tool run dotnet-ef database update --project src/Services/Core/DeliveryOps.Core.Infrastructure --startup-project src/Services/Core/DeliveryOps.Core.Api
	cd backend && dotnet tool run dotnet-ef database update --project src/Services/Auth/DeliveryOps.Auth.Api --startup-project src/Services/Auth/DeliveryOps.Auth.Api
	cd backend && dotnet tool run dotnet-ef database update --project src/Services/Notifications/DeliveryOps.Notifications.Api --startup-project src/Services/Notifications/DeliveryOps.Notifications.Api
	cd backend && dotnet tool run dotnet-ef database update --project src/Services/Integrations/DeliveryOps.Integrations.Api --startup-project src/Services/Integrations/DeliveryOps.Integrations.Api

seed:
	node scripts/local-seed.mjs

smoke:
	./scripts/local-smoke.sh

e2e:
	npm --prefix apps/admin-web run test:e2e

webhook-e2e:
	node scripts/provider-webhook-simulator.mjs all

webhook-yemeksepeti:
	node scripts/provider-webhook-simulator.mjs yemeksepeti

webhook-getir:
	node scripts/provider-webhook-simulator.mjs getir

mobile-e2e:
	node scripts/mobile-e2e.mjs

check:
	SKIP_SMOKE=1 SKIP_E2E=1 ./scripts/local-check.sh

test:
	./scripts/local-check.sh

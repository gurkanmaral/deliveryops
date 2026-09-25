#!/bin/sh
set -eu

dotnet tool run dotnet-ef database update \
  --project src/Services/Core/DeliveryOps.Core.Infrastructure \
  --startup-project src/Services/Core/DeliveryOps.Core.Api \
  --configuration Release --no-build

dotnet tool run dotnet-ef database update \
  --project src/Services/Auth/DeliveryOps.Auth.Api \
  --startup-project src/Services/Auth/DeliveryOps.Auth.Api \
  --configuration Release --no-build

dotnet tool run dotnet-ef database update \
  --project src/Services/Notifications/DeliveryOps.Notifications.Api \
  --startup-project src/Services/Notifications/DeliveryOps.Notifications.Api \
  --configuration Release --no-build

dotnet tool run dotnet-ef database update \
  --project src/Services/Integrations/DeliveryOps.Integrations.Api \
  --startup-project src/Services/Integrations/DeliveryOps.Integrations.Api \
  --configuration Release --no-build

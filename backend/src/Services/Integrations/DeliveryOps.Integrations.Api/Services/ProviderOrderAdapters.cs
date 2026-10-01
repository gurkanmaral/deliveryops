using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeliveryOps.Integrations.Api.Domain;

namespace DeliveryOps.Integrations.Api.Services;

public sealed record AdaptedOrder(string ExternalEventId, string EventType,
    InboundOrderRequest Order, string NormalizedPayload, string PayloadHash);

public interface IOrderProviderAdapter
{
    bool CanHandle(IntegrationProvider provider, string adapterVersion);
    AdaptedOrder Adapt(string rawPayload);
}

public sealed class CanonicalV1OrderAdapter : IOrderProviderAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { PropertyNameCaseInsensitive = true };

    public bool CanHandle(IntegrationProvider provider, string adapterVersion) =>
        string.Equals(adapterVersion, "canonical-v1", StringComparison.OrdinalIgnoreCase);

    public AdaptedOrder Adapt(string rawPayload)
    {
        CanonicalOrderEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<CanonicalOrderEnvelope>(rawPayload, JsonOptions); }
        catch (JsonException exception) { throw new ProviderPayloadException("Webhook JSON formatı geçersiz.", exception); }
        if (envelope is null) throw new ProviderPayloadException("Webhook gövdesi boş olamaz.");
        ProviderCoordinates coordinates = ProviderPayload.ReadCoordinates(envelope.DeliveryLatitude,
            envelope.DeliveryLongitude, "canonical");
        string eventType = string.IsNullOrWhiteSpace(envelope.EventType) ? "order.created" : envelope.EventType.Trim();
        InboundPayment? payment = ReadPayment(envelope.Payment);
        if (string.Equals(eventType, "order.paid", StringComparison.OrdinalIgnoreCase) &&
            (payment is null || !payment.IsPaid))
            throw new ProviderPayloadException("order.paid olayında ödenmiş bir payment bloğu zorunludur.");
        InboundOrderRequest order = new(envelope.ExternalOrderId, envelope.CustomerName,
            envelope.CustomerPhone, envelope.DeliveryAddress, envelope.TotalAmount, coordinates.Latitude,
            coordinates.Longitude, ProviderPayload.NormalizeOptional(envelope.DeliveryInstructions),
            envelope.DeliveryFulfillment, payment);
        ProviderPayload.ValidateOrder(order);
        string eventId = string.IsNullOrWhiteSpace(envelope.EventId) ? order.ExternalOrderId : envelope.EventId.Trim();
        return ProviderPayload.Complete(rawPayload, eventId, eventType, order);
    }

    private static InboundPayment? ReadPayment(CanonicalPayment? payment)
    {
        if (payment is null) return null;
        InboundPaymentMethod method = payment.Method?.Trim().ToLowerInvariant() switch
        {
            "cash" or "nakit" => InboundPaymentMethod.Cash,
            "card" or "kart" or "credit_card" or "creditcard" => InboundPaymentMethod.Card,
            "online" => InboundPaymentMethod.Online,
            _ => throw new ProviderPayloadException("payment.method cash, card veya online olmalıdır.")
        };
        string? status = payment.Status?.Trim().ToLowerInvariant();
        if (status is not (null or "" or "paid" or "unpaid"))
            throw new ProviderPayloadException("payment.status paid veya unpaid olmalıdır.");
        if (payment.Amount is < 0 or > 100_000_000)
            throw new ProviderPayloadException("payment.amount geçersiz.");
        string? reference = ProviderPayload.NormalizeOptional(payment.Reference);
        if (reference?.Length > 100) throw new ProviderPayloadException("payment.reference en fazla 100 karakter olabilir.");
        return new InboundPayment(method, status == "paid", payment.Amount, reference, payment.PaidAtUtc);
    }

    private sealed record CanonicalOrderEnvelope(string? EventId, string? EventType, string ExternalOrderId,
        string CustomerName, string CustomerPhone, string DeliveryAddress, decimal TotalAmount,
        double? DeliveryLatitude, double? DeliveryLongitude, string? DeliveryInstructions,
        ProviderDeliveryFulfillment DeliveryFulfillment = ProviderDeliveryFulfillment.MerchantCourier,
        CanonicalPayment? Payment = null);

    private sealed record CanonicalPayment(string? Method, string? Status, decimal? Amount, string? Reference,
        DateTimeOffset? PaidAtUtc);
}

public sealed class YemeksepetiPartnerV2OrderAdapter : IOrderProviderAdapter
{
    public bool CanHandle(IntegrationProvider provider, string adapterVersion) =>
        provider == IntegrationProvider.Yemeksepeti &&
        string.Equals(adapterVersion, "yemeksepeti-partner-v2", StringComparison.OrdinalIgnoreCase);

    public AdaptedOrder Adapt(string rawPayload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayload);
            JsonElement root = document.RootElement;
            string orderId = ProviderPayload.RequiredString(root, "order_id", "Yemeksepeti");
            string status = ProviderPayload.RequiredString(root, "status", "Yemeksepeti").ToUpperInvariant();
            if (status is not ("RECEIVED" or "READY_FOR_PICKUP" or "DISPATCHED" or "CANCELLED" or "DELIVERED"))
                throw new ProviderPayloadException($"Desteklenmeyen Yemeksepeti sipariş durumu: {status}.");
            JsonElement customer = ProviderPayload.RequiredObject(root, "customer", "Yemeksepeti");
            string customerName = $"{ProviderPayload.OptionalString(customer, "first_name")} {ProviderPayload.OptionalString(customer, "last_name")}".Trim();
            if (string.IsNullOrWhiteSpace(customerName)) customerName = "Yemeksepeti Müşterisi";
            string phone = ProviderPayload.OptionalString(customer, "phone_number");
            if (string.IsNullOrWhiteSpace(phone)) phone = "Yemeksepeti maskeli telefon";
            string orderType = ProviderPayload.OptionalString(root, "order_type");
            JsonElement? delivery = ProviderPayload.OptionalObject(customer, "delivery_address");
            string address = string.Equals(orderType, "PICKUP", StringComparison.OrdinalIgnoreCase)
                ? "Yemeksepeti mağazadan teslim" : BuildAddress(delivery);
            ProviderCoordinates coordinates = delivery.HasValue
                ? ProviderPayload.ReadCoordinates(delivery.Value, "latitude", "longitude", "Yemeksepeti")
                : default;
            string? instructions = delivery.HasValue
                ? ProviderPayload.NormalizeOptional(ProviderPayload.OptionalString(delivery.Value, "instructions"))
                : null;
            JsonElement payment = ProviderPayload.RequiredObject(root, "payment", "Yemeksepeti");
            decimal total = ProviderPayload.RequiredDecimal(payment, "order_total", "Yemeksepeti");
            InboundOrderRequest order = new(orderId, customerName, phone, address, total,
                coordinates.Latitude, coordinates.Longitude, instructions,
                ResolveYemeksepetiFulfillment(root, orderType));
            ProviderPayload.ValidateOrder(order);
            string updatedAt = root.TryGetProperty("sys", out JsonElement sys)
                ? ProviderPayload.OptionalString(sys, "updated_at") : string.Empty;
            string payloadHash = ProviderPayload.Hash(rawPayload);
            string eventId = string.IsNullOrWhiteSpace(updatedAt)
                ? $"{orderId}:{status}:{payloadHash[..12]}" : $"{orderId}:{status}:{updatedAt}";
            return ProviderPayload.Complete(rawPayload, eventId, $"order.{status.ToLowerInvariant()}", order, payloadHash);
        }
        catch (JsonException exception)
        {
            throw new ProviderPayloadException("Yemeksepeti webhook JSON formatı geçersiz.", exception);
        }
    }

    private static string BuildAddress(JsonElement? address)
    {
        if (!address.HasValue) return "Yemeksepeti teslimat adresi";
        string formatted = ProviderPayload.OptionalString(address.Value, "formattedAddress");
        if (!string.IsNullOrWhiteSpace(formatted)) return formatted;
        string[] parts = ["street", "number", "building", "apartment", "floor", "suburb", "city"];
        string result = string.Join(", ", parts.Select(x => ProviderPayload.OptionalString(address.Value, x))
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(result) ? "Yemeksepeti teslimat adresi" : result;
    }

    private static ProviderDeliveryFulfillment ResolveYemeksepetiFulfillment(JsonElement root,
        string orderType)
    {
        if (string.Equals(orderType, "PICKUP", StringComparison.OrdinalIgnoreCase))
            return ProviderDeliveryFulfillment.CustomerPickup;
        string transportType = ProviderPayload.OptionalString(root, "transport_type");
        return string.Equals(transportType, "LOGISTICS_DELIVERY", StringComparison.OrdinalIgnoreCase)
            ? ProviderDeliveryFulfillment.ProviderCourier
            : ProviderDeliveryFulfillment.MerchantCourier;
    }
}

public sealed class GetirFoodV1OrderAdapter : IOrderProviderAdapter
{
    public bool CanHandle(IntegrationProvider provider, string adapterVersion) =>
        provider == IntegrationProvider.Getir &&
        string.Equals(adapterVersion, "getir-food-v1", StringComparison.OrdinalIgnoreCase);

    public AdaptedOrder Adapt(string rawPayload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayload);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("foodOrder", out JsonElement wrapped) && wrapped.ValueKind == JsonValueKind.Object)
                root = wrapped;
            string orderId = ProviderPayload.RequiredString(root, "id", "Getir");
            JsonElement client = ProviderPayload.RequiredObject(root, "client", "Getir");
            string name = ProviderPayload.OptionalString(client, "name");
            if (string.IsNullOrWhiteSpace(name)) name = "Getir Müşterisi";
            string phone = ProviderPayload.FirstString(client, "clientUnmaskedPhoneNumber", "contactPhoneNumber", "clientPhoneNumber");
            if (string.IsNullOrWhiteSpace(phone)) phone = "Getir maskeli telefon";
            JsonElement addressObject = ProviderPayload.RequiredObject(client, "deliveryAddress", "Getir");
            string address = ProviderPayload.OptionalString(addressObject, "address");
            if (string.IsNullOrWhiteSpace(address))
                address = string.Join(", ", new[] { "district", "city" }.Select(x => ProviderPayload.OptionalString(addressObject, x))
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
            if (string.IsNullOrWhiteSpace(address)) address = "Getir teslimat adresi";
            JsonElement location = ProviderPayload.RequiredObject(client, "location", "Getir");
            ProviderCoordinates coordinates = ProviderPayload.ReadCoordinates(location, "lat", "lon", "Getir");
            string? instructions = ProviderPayload.NormalizeOptional(string.Join(" · ", new[]
                {
                    ProviderPayload.OptionalString(addressObject, "description"),
                    ProviderPayload.OptionalString(root, "clientNote"),
                    IsTrue(root, "doNotKnock") ? "Zili çalmayın" : string.Empty,
                    IsTrue(root, "dropOffAtDoor") ? "Kapıya bırakın" : string.Empty,
                    PaymentText(root)
                }
                .Where(x => !string.IsNullOrWhiteSpace(x))));
            if (instructions?.Length > 2000) instructions = instructions[..2000];
            decimal total = ProviderPayload.OptionalDecimal(root, "totalDiscountedPrice")
                ?? ProviderPayload.RequiredDecimal(root, "totalPrice", "Getir");
            InboundOrderRequest order = new(orderId, name, phone, address, total,
                coordinates.Latitude, coordinates.Longitude, instructions, ResolveFulfillment(root));
            ProviderPayload.ValidateOrder(order);
            string hash = ProviderPayload.Hash(rawPayload);
            // Getir posts cancellations to a second URL with the same order shape plus the cancel fields;
            // both URLs may point here, so the payload decides which event it is.
            if (IsCancellation(root))
                return ProviderPayload.Complete(rawPayload, $"{orderId}:cancelled", "order.cancelled", order, hash);
            string status = ProviderPayload.OptionalStringOrNumber(root, "status");
            string checkoutDate = ProviderPayload.OptionalString(root, "checkoutDate");
            string eventId = $"{orderId}:{(string.IsNullOrWhiteSpace(status) ? "created" : status)}:{(string.IsNullOrWhiteSpace(checkoutDate) ? hash[..12] : checkoutDate)}";
            return ProviderPayload.Complete(rawPayload, eventId, "order.created", order, hash);
        }
        catch (JsonException exception)
        {
            throw new ProviderPayloadException("Getir webhook JSON formatı geçersiz.", exception);
        }
    }

    // deliveryType 1 = Getir courier, 2 = restaurant's own courier (GetirFood API docs).
    private static ProviderDeliveryFulfillment ResolveFulfillment(JsonElement root) =>
        ProviderPayload.OptionalStringOrNumber(root, "deliveryType") switch
        {
            "1" => ProviderDeliveryFulfillment.ProviderCourier,
            "2" => ProviderDeliveryFulfillment.MerchantCourier,
            _ => ProviderPayload.OptionalObject(root, "courier").HasValue
                ? ProviderDeliveryFulfillment.ProviderCourier
                : ProviderDeliveryFulfillment.MerchantCourier
        };

    private static bool IsCancellation(JsonElement root) =>
        !string.IsNullOrWhiteSpace(ProviderPayload.OptionalString(root, "cancelDate")) ||
        ProviderPayload.OptionalObject(root, "cancelReason").HasValue;

    private static bool IsTrue(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static string PaymentText(JsonElement root)
    {
        JsonElement? text = ProviderPayload.OptionalObject(root, "paymentMethodText");
        string value = text.HasValue ? ProviderPayload.OptionalString(text.Value, "tr") : string.Empty;
        return string.IsNullOrWhiteSpace(value) ? string.Empty : $"Ödeme: {value.Trim()}";
    }
}

public sealed class TrendyolWebhookV1OrderAdapter : IOrderProviderAdapter
{
    public bool CanHandle(IntegrationProvider provider, string adapterVersion) =>
        provider == IntegrationProvider.Trendyol &&
        string.Equals(adapterVersion, "trendyol-webhook-v1", StringComparison.OrdinalIgnoreCase);

    public AdaptedOrder Adapt(string rawPayload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawPayload);
            JsonElement root = document.RootElement;
            JsonElement package = root;
            if (root.TryGetProperty("content", out JsonElement content))
            {
                if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() != 1)
                    throw new ProviderPayloadException("Trendyol Go webhook içeriği tam olarak bir sipariş paketi içermelidir.");
                package = content[0];
            }
            string packageId = ProviderPayload.OptionalStringOrNumber(package, "id");
            string orderNumber = ProviderPayload.RequiredStringOrNumber(package, "orderNumber", "Trendyol Go");
            string externalId = string.IsNullOrWhiteSpace(packageId) ? orderNumber : $"{orderNumber}-{packageId}";
            string status = ProviderPayload.FirstString(package, "packageStatus", "status");
            if (string.IsNullOrWhiteSpace(status)) status = "Delivered";
            // A package accepted on Trendyol's own tablet before we read it is still a new order for us.
            if (string.Equals(status, "Picking", StringComparison.OrdinalIgnoreCase)) status = "Created";
            bool isCreated = string.Equals(status, "Created", StringComparison.OrdinalIgnoreCase);
            InboundOrderRequest order = isCreated
                ? BuildTrendyolCreatedOrder(package, externalId)
                : new InboundOrderRequest(externalId, "Trendyol Go Müşterisi", "Trendyol Go maskeli telefon",
                    "Trendyol Go teslimat adresi", 0);
            string hash = ProviderPayload.Hash(rawPayload);
            // Trendyol re-sends (and polling re-reads) the same package many times; package id + status is the
            // documented idempotency key, so every later read of a status maps to the same event.
            string eventId = $"{externalId}:{status.ToUpperInvariant()}";
            return ProviderPayload.Complete(rawPayload, eventId, $"order.{status.ToLowerInvariant()}", order, hash);
        }
        catch (JsonException exception)
        {
            throw new ProviderPayloadException("Trendyol webhook JSON formatı geçersiz.", exception);
        }
    }

    private static InboundOrderRequest BuildTrendyolCreatedOrder(JsonElement package, string externalId)
    {
        JsonElement addressObject = ProviderPayload.OptionalObject(package, "address")
            ?? ProviderPayload.RequiredObject(package, "shipmentAddress", "Trendyol Go");
        JsonElement? customer = ProviderPayload.OptionalObject(package, "customer");
        string name = $"{ProviderPayload.OptionalString(addressObject, "firstName")} {ProviderPayload.OptionalString(addressObject, "lastName")}".Trim();
        if (string.IsNullOrWhiteSpace(name) && customer.HasValue)
            name = $"{ProviderPayload.OptionalString(customer.Value, "firstName")} {ProviderPayload.OptionalString(customer.Value, "lastName")}".Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "Trendyol Go Müşterisi";
        string phone = ProviderPayload.OptionalString(addressObject, "phone");
        if (string.IsNullOrWhiteSpace(phone)) phone = "Trendyol Go maskeli telefon";
        string address = TrendyolAddress(addressObject);
        if (string.IsNullOrWhiteSpace(address)) address = "Trendyol Go teslimat adresi";
        // Orders delivered by the Trendyol courier carry "TGO Yemek" placeholders instead of coordinates.
        ProviderCoordinates coordinates = IsPlaceholder(ProviderPayload.OptionalString(addressObject, "latitude"))
            ? default
            : ProviderPayload.ReadCoordinates(addressObject, "latitude", "longitude", "Trendyol Go");
        string pinCode = ProviderPayload.OptionalStringOrNumber(addressObject, "pinCode");
        string callCenter = ProviderPayload.OptionalString(package, "callCenterPhone");
        string orderCode = ProviderPayload.OptionalString(package, "orderCode");
        string? instructions = ProviderPayload.NormalizeOptional(string.Join(" · ", new[]
        {
            Clean(ProviderPayload.OptionalString(addressObject, "addressDescription")),
            ProviderPayload.OptionalString(package, "customerNote"),
            customer.HasValue ? ProviderPayload.OptionalString(customer.Value, "note") : string.Empty,
            string.IsNullOrWhiteSpace(orderCode) ? string.Empty : $"Sipariş kodu: {orderCode}",
            // Customer phone numbers are masked: the courier calls the given number and enters the pin code.
            string.IsNullOrWhiteSpace(pinCode) ? string.Empty
                : $"Müşteri arama: {(string.IsNullOrWhiteSpace(callCenter) ? phone : callCenter)} · kod {pinCode}",
            TrendyolPaymentText(package)
        }.Where(x => !string.IsNullOrWhiteSpace(x))));
        if (instructions is { Length: > 2000 }) instructions = instructions[..2000];
        decimal total = ProviderPayload.OptionalDecimal(package, "totalPrice")
            ?? ProviderPayload.OptionalDecimal(package, "packageGrossAmount") ?? 0;
        bool pickup = ProviderPayload.OptionalBoolean(package, "storePickupSelected") == true;
        string deliveryType = ProviderPayload.FirstString(package, "deliveryType", "deliveryModel");
        ProviderDeliveryFulfillment fulfillment = pickup
            ? ProviderDeliveryFulfillment.CustomerPickup
            : string.Equals(deliveryType, "GO", StringComparison.OrdinalIgnoreCase)
                ? ProviderDeliveryFulfillment.ProviderCourier
                : ProviderDeliveryFulfillment.MerchantCourier;
        InboundOrderRequest order = new(externalId, name, phone, address, total,
            coordinates.Latitude, coordinates.Longitude, instructions, fulfillment, TrendyolPayment(package, total));
        ProviderPayload.ValidateOrder(order);
        return order;
    }

    private static bool IsPlaceholder(string value) =>
        value.Contains("TGO Yemek", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Trendyol Go", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("TGO Hızlı Market", StringComparison.OrdinalIgnoreCase);

    private static string Clean(string value) => IsPlaceholder(value) ? string.Empty : value;

    /// <summary>
    /// address1 holds the real street address (and, for Uber stores, the free-text apartment/floor/door) and
    /// must not be parsed; the separate apartment fields are appended only while they still carry real values.
    /// </summary>
    private static string TrendyolAddress(JsonElement address)
    {
        string main = Clean(ProviderPayload.FirstString(address, "address1", "fullAddress", "shortAddress"));
        if (string.IsNullOrWhiteSpace(main)) return string.Empty;
        List<string> parts = [main];
        foreach ((string field, string label) in new[] { ("apartmentNumber", "Bina"), ("floor", "Kat"), ("doorNumber", "Daire") })
        {
            string value = Clean(ProviderPayload.OptionalStringOrNumber(address, field));
            if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{label} {value}");
        }
        foreach (string field in new[] { "neighborhood", "district", "city" })
        {
            string value = Clean(ProviderPayload.OptionalString(address, field));
            if (!string.IsNullOrWhiteSpace(value)) parts.Add(value);
        }
        return string.Join(", ", parts);
    }

    private static string OnDeliveryType(JsonElement package)
    {
        JsonElement? payment = ProviderPayload.OptionalObject(package, "payment");
        if (!payment.HasValue || !payment.Value.TryGetProperty("onDelivery", out JsonElement onDelivery)) return string.Empty;
        if (onDelivery.ValueKind == JsonValueKind.String) return onDelivery.GetString()?.Trim().ToUpperInvariant() ?? string.Empty;
        if (onDelivery.ValueKind != JsonValueKind.Object) return string.Empty;
        return ProviderPayload.FirstString(onDelivery, "paymentType", "type", "name").ToUpperInvariant();
    }

    private static InboundPayment? TrendyolPayment(JsonElement package, decimal total)
    {
        JsonElement? payment = ProviderPayload.OptionalObject(package, "payment");
        if (!payment.HasValue) return null;
        string type = ProviderPayload.OptionalString(payment.Value, "paymentType").ToUpperInvariant();
        if (type == "PAY_WITH_ON_DELIVERY")
            return new InboundPayment(OnDeliveryType(package) == "CASH" ? InboundPaymentMethod.Cash : InboundPaymentMethod.Card,
                false, total);
        return string.IsNullOrWhiteSpace(type) ? null : new InboundPayment(InboundPaymentMethod.Online, true, total);
    }

    private static string TrendyolPaymentText(JsonElement package)
    {
        JsonElement? payment = ProviderPayload.OptionalObject(package, "payment");
        if (!payment.HasValue) return string.Empty;
        string type = ProviderPayload.OptionalString(payment.Value, "paymentType").ToUpperInvariant();
        if (type != "PAY_WITH_ON_DELIVERY") return string.IsNullOrWhiteSpace(type) ? string.Empty : "Ödeme: Online ödendi";
        string onDelivery = OnDeliveryType(package);
        string label = onDelivery switch
        {
            "CASH" => "nakit",
            "CARD" => "kredi kartı",
            "" => "belirtilmedi",
            _ => onDelivery.Replace('_', ' ').ToLowerInvariant()
        };
        return $"Ödeme: Kapıda {label}";
    }
}

internal readonly record struct ProviderCoordinates(double? Latitude, double? Longitude);

internal static class ProviderPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AdaptedOrder Complete(string rawPayload, string eventId, string eventType,
        InboundOrderRequest order, string? hash = null) => new(eventId, eventType, order,
        JsonSerializer.Serialize(order, JsonOptions), hash ?? Hash(rawPayload));

    public static string Hash(string rawPayload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload))).ToLowerInvariant();

    public static void ValidateOrder(InboundOrderRequest order)
    {
        if (string.IsNullOrWhiteSpace(order.ExternalOrderId) || string.IsNullOrWhiteSpace(order.CustomerName) ||
            string.IsNullOrWhiteSpace(order.CustomerPhone) || string.IsNullOrWhiteSpace(order.DeliveryAddress) ||
            order.TotalAmount < 0)
            throw new ProviderPayloadException("Sipariş numarası, müşteri, telefon, adres ve geçerli tutar zorunludur.");
    }

    public static ProviderCoordinates ReadCoordinates(JsonElement element, string latitudeName,
        string longitudeName, string provider) => ReadCoordinates(OptionalDouble(element, latitudeName),
        OptionalDouble(element, longitudeName), provider);

    public static ProviderCoordinates ReadCoordinates(double? latitude, double? longitude, string provider)
    {
        if (!latitude.HasValue && !longitude.HasValue) return default;
        if (latitude.HasValue != longitude.HasValue || !double.IsFinite(latitude!.Value) ||
            !double.IsFinite(longitude!.Value) || latitude is < -90 or > 90 || longitude is < -180 or > 180 ||
            latitude == 0 && longitude == 0)
            throw new ProviderPayloadException($"{provider} teslimat koordinatları geçersiz.");
        return new ProviderCoordinates(latitude, longitude);
    }

    public static JsonElement RequiredObject(JsonElement element, string name, string provider) =>
        OptionalObject(element, name) ?? throw new ProviderPayloadException($"{provider} {name} alanı zorunludur.");

    public static JsonElement? OptionalObject(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object ? value : null;

    public static string RequiredString(JsonElement element, string name, string provider)
    {
        string value = OptionalString(element, name);
        return !string.IsNullOrWhiteSpace(value) ? value
            : throw new ProviderPayloadException($"{provider} {name} alanı zorunludur.");
    }

    public static string RequiredStringOrNumber(JsonElement element, string name, string provider)
    {
        string value = OptionalStringOrNumber(element, name);
        return !string.IsNullOrWhiteSpace(value) ? value
            : throw new ProviderPayloadException($"{provider} {name} alanı zorunludur.");
    }

    public static string OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty : string.Empty;

    public static string OptionalStringOrNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    public static string FirstString(JsonElement element, params string[] names) =>
        names.Select(name => OptionalString(element, name)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    public static string FirstStringOrNumber(JsonElement element, params string[] names) =>
        names.Select(name => OptionalStringOrNumber(element, name)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    public static bool? OptionalBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : null;

    public static decimal RequiredDecimal(JsonElement element, string name, string provider) =>
        OptionalDecimal(element, name) is { } value && value >= 0 ? value
            : throw new ProviderPayloadException($"{provider} {name} alanı geçersiz.");

    public static decimal? OptionalDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number)) return number;
        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(),
            NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : null;
    }

    private static double? OptionalDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number)) return number;
        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(),
            NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : null;
    }

    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ProviderAdapterRegistry(IEnumerable<IOrderProviderAdapter> adapters)
{
    public IOrderProviderAdapter Resolve(IntegrationProvider provider, string adapterVersion) =>
        adapters.FirstOrDefault(x => x.CanHandle(provider, adapterVersion)) ??
        throw new ProviderPayloadException($"{provider} için '{adapterVersion}' adaptörü kayıtlı değil.");
}

public sealed class ProviderPayloadException(string message, Exception? innerException = null)
    : Exception(message, innerException);

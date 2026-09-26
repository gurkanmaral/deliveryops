using DeliveryOps.Core.Queries.Branches;
using DeliveryOps.Core.Queries.Businesses;
using DeliveryOps.Core.Queries.Couriers;
using DeliveryOps.Core.Queries.Orders;
using DeliveryOps.Core.Queries.Locations;
using DeliveryOps.Core.Domain.Enums;
using FluentValidation;

namespace DeliveryOps.Core.Handlers.Validation;

public sealed class CreateBusinessValidator : AbstractValidator<CreateBusinessCommand>
{
    public CreateBusinessValidator() { RuleFor(x => x.Name).NotEmpty().MaximumLength(160); RuleFor(x => x.Code).NotEmpty().MaximumLength(40).Matches("^[A-Za-z0-9-]+$"); }
}
public sealed class CreateBranchValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchValidator() { RuleFor(x => x.BusinessId).NotEmpty(); RuleFor(x => x.Name).NotEmpty().MaximumLength(160); RuleFor(x => x.Address).NotEmpty().MaximumLength(500); RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue); RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue); }
}
public sealed class UpdateBranchValidator : AbstractValidator<UpdateBranchCommand>
{
    public UpdateBranchValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Name).NotEmpty().MaximumLength(160); RuleFor(x => x.Address).NotEmpty().MaximumLength(500); }
}
public sealed class CreateCourierValidator : AbstractValidator<CreateCourierCommand>
{
    public CreateCourierValidator() { RuleFor(x => x.BusinessId).NotEmpty(); RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100); RuleFor(x => x.LastName).NotEmpty().MaximumLength(100); RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30).Matches("^[+0-9 ]+$"); }
}
public sealed class UpdateCourierValidator : AbstractValidator<UpdateCourierCommand>
{
    public UpdateCourierValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100); RuleFor(x => x.LastName).NotEmpty().MaximumLength(100); RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(30); }
}
public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.BusinessId).NotEmpty(); RuleFor(x => x.BranchId).NotEmpty();
        RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(160);
        RuleFor(x => x.CustomerPhone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.DeliveryAddress).NotEmpty().MaximumLength(500);
        RuleFor(x => x.DeliveryInstructions).MaximumLength(1000);
        RuleFor(x => x.TotalAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeliveryLatitude).InclusiveBetween(-90, 90).When(x => x.DeliveryLatitude.HasValue);
        RuleFor(x => x.DeliveryLongitude).InclusiveBetween(-180, 180).When(x => x.DeliveryLongitude.HasValue);
        RuleFor(x => x).Must(x => x.DeliveryLatitude.HasValue == x.DeliveryLongitude.HasValue)
            .WithMessage("Teslimat enlem ve boylamı birlikte gönderilmelidir.");
        RuleFor(x => x).Must(x => !x.DeliveryLatitude.HasValue ||
                                  x.DeliveryLocationSource != DeliveryOps.Core.Domain.Enums.DeliveryLocationSource.Unknown &&
                                  x.DeliveryLocationAccuracy != DeliveryOps.Core.Domain.Enums.DeliveryLocationAccuracy.Unknown)
            .WithMessage("Koordinat kaynağı ve doğruluğu zorunludur.");
    }
}

public sealed class RecordCourierLocationValidator : AbstractValidator<RecordCourierLocationCommand>
{
    public RecordCourierLocationValidator()
    {
        RuleFor(x => x.CourierId).NotEmpty();
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.AccuracyMeters).GreaterThanOrEqualTo(0).When(x => x.AccuracyMeters.HasValue);
        RuleFor(x => x.SpeedMetersPerSecond).InclusiveBetween(0, 100).When(x => x.SpeedMetersPerSecond.HasValue);
        RuleFor(x => x.HeadingDegrees).InclusiveBetween(0, 360).When(x => x.HeadingDegrees.HasValue);
    }
}
public sealed class UpdateOrderValidator : AbstractValidator<UpdateOrderCommand>
{
    public UpdateOrderValidator()
    {
        RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.CustomerName).NotEmpty().MaximumLength(160);
        RuleFor(x => x.CustomerPhone).NotEmpty().MaximumLength(30);
        RuleFor(x => x.DeliveryAddress).NotEmpty().MaximumLength(500);
        RuleFor(x => x.DeliveryInstructions).MaximumLength(1000);
        RuleFor(x => x.TotalAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DeliveryLatitude).InclusiveBetween(-90, 90).When(x => x.DeliveryLatitude.HasValue);
        RuleFor(x => x.DeliveryLongitude).InclusiveBetween(-180, 180).When(x => x.DeliveryLongitude.HasValue);
        RuleFor(x => x).Must(x => x.DeliveryLatitude.HasValue == x.DeliveryLongitude.HasValue)
            .WithMessage("Teslimat enlem ve boylamı birlikte gönderilmelidir.");
        RuleFor(x => x).Must(x => !x.DeliveryLatitude.HasValue ||
                                  x.DeliveryLocationSource != DeliveryLocationSource.Unknown &&
                                  x.DeliveryLocationAccuracy != DeliveryLocationAccuracy.Unknown)
            .WithMessage("Koordinat kaynağı ve doğruluğu zorunludur.");
    }
}
public sealed class ReportDeliveryFailureValidator : AbstractValidator<ReportDeliveryFailureCommand>
{
    public ReportDeliveryFailureValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Reason).NotEmpty().MaximumLength(500); }
}

using DeliveryOps.BuildingBlocks.Domain;

namespace DeliveryOps.Core.Domain.Entities;

public sealed class Business : Entity
{
    private Business() { }

    private Business(string name, string code)
    {
        Name = name;
        Code = code;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public ICollection<Branch> Branches { get; private set; } = [];

    public static Business Create(string name, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new Business(name.Trim(), code.Trim().ToUpperInvariant());
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        MarkAsUpdated();
    }

    public void Update(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        MarkAsUpdated();
    }
}
